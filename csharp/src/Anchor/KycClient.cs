using KeetaNet.Anchor;
using KeetaNet.Anchor.Crypto;
using KeetaNet.Examples.Common;
using CryptoCertificate = KeetaNet.Anchor.Crypto.Certificate;
using IssuedCertificate = KeetaNet.Anchor.Certificate;

namespace KeetaNet.Examples.Anchor;

public sealed class KycClientExample : IKeetaExample
{
	private const KeetaNetwork Network = KeetaNetwork.Test;
	private static readonly string[] Countries = ["US"];

	public string Id => "anchor/kyc-client";

	public string Description =>
		"Example of using the Keeta Anchor Client to programmatically add KYC to a wallet";

	public async Task<int> Run(string[] args, CancellationToken cancellationToken = default)
	{
		Console.WriteLine("""
			Keeta KYC Example: Add KYC Verification to a Wallet
			====================================================
			""");

		using var runtime = WasmRuntime.Load();
		string seed = runtime.Accounts.GenerateRandomSeed();
		using Account userAccount = runtime.Accounts.FromSeed(seed, 0, "ecdsa_secp256k1");

		Console.WriteLine($"Seed: {seed}");
		Console.WriteLine($"Keeta Account: {userAccount.PublicKeyString}\n");

		using UserClient userClient = runtime.CreateUserClient(Network, userAccount);

		if (!await Helper.GetFaucetTokens(userClient, Network, cancellationToken))
		{
			throw new InvalidOperationException("Failed to get Faucet Tokens");
		}

		using KycClient kycClient = runtime.CreateKycClient(
			Network.RepresentativeApiUrl(),
			Constants.MetadataRoot,
			userAccount);

		SupportedCountries supportedCountries = await kycClient.GetSupportedCountries(cancellationToken);
		Console.WriteLine(
			"Supported Countries: {0}",
			supportedCountries.Worldwide ? "Worldwide" : string.Join(", ", supportedCountries.Countries));

		IReadOnlyList<KycProvider> providers = await kycClient.GetProviders(Countries, cancellationToken);

		if (providers.Count == 0)
		{
			throw new InvalidOperationException("No KYC providers found on this network");
		}

		const string countryCode = "US";
		Console.WriteLine($"\nUsing country: {countryCode}\n");

		KycProvider? provider = providers.FirstOrDefault(candidate => candidate.Id == Constants.FootprintProviderId);
		if (provider is null)
		{
			throw new InvalidOperationException("Footprint KYC provider not found");
		}

		using CryptoCertificate providerCa = provider.GetCA();
		Console.WriteLine($"Found KYC provider: {provider.Id} ({providerCa.Subject})");

		VerificationOutcome created = await provider.StartVerification(Countries, cancellationToken: cancellationToken);
		if (created.Ready is null)
		{
			throw new InvalidOperationException("Verification was not ready immediately after creation");
		}

		Verification verification = created.Ready;
		Console.WriteLine($"""
			
			Verification started:
			  Request ID: {verification.Id}
			  Expected Cost: {verification.ExpectedCost.Min} - {verification.ExpectedCost.Max}

			========================================
			 COMPLETE YOUR KYC VERIFICATION
			========================================
			Visit the following URL to complete KYC:

			***************************************
			IMPORTANT: For onboarding with Bivo in sandbox
			- Use "three" in Address Line 2 (optional Apt. Suite field)
			- Use a unique SSN / Tax ID
			Share KYC with Bivo will then be automatically approved. Otherwise the Share KYC request will timeout.
			***************************************

			{verification.WebUrl}

			========================================
			""");

		string shouldPoll = Helper.ReadLine(
			"Have you completed the verification? Press Enter to start polling for your certificate (or type \"skip\" to exit): ");
		if (shouldPoll.Trim().Equals("skip", StringComparison.OrdinalIgnoreCase))
		{
			Console.WriteLine("Exiting. You can poll for your certificate later using the verification ID.");
			return 0;
		}

		Console.WriteLine("Polling for KYC certificate...");
		while (!cancellationToken.IsCancellationRequested)
		{
			CertificatesOutcome results = await provider.GetCertificates(verification.Id, cancellationToken);
			if (results.Ready is null)
			{
				Console.Write('.');
				await Task.Delay(TimeSpan.FromMilliseconds(results.RetryAfterMs ?? 500), cancellationToken);
				continue;
			}

			Console.WriteLine($"\n\nKYC Certificate received!");
			Console.WriteLine($"  Number of certificates: {results.Ready.Results.Count}");

			int index = 0;
			foreach (IssuedCertificate issued in results.Ready.Results)
			{
				index++;
				using KycCertificate certificate = runtime.KycCertificates.Parse(issued.Value);
				Console.WriteLine($"\n  Certificate {index}:");
				using CryptoCertificate baseCertificate = certificate.Base();
				Console.WriteLine($"    Subject: {baseCertificate.Subject}");
				Console.WriteLine($"    Valid: {certificate.IsValidAt(DateTimeOffset.UtcNow)}");

				if (issued.Intermediates.Count > 0)
				{
					Console.WriteLine($"    Intermediate certificates: {issued.Intermediates.Count}");
				}

				if (certificate.GetAttributeNames().Contains("fullName", StringComparer.Ordinal))
				{
					KycAttributeValue fullName = certificate.GetAttribute("fullName", userAccount);
					Console.WriteLine($"    Full name (decrypted): {fullName.AsText()}");
				}

				await PublishCertificate(runtime, userClient, issued, cancellationToken).ConfigureAwait(false);
			}

			IReadOnlyList<IssuedCertificate> onChain = await userClient.GetAllCertificates(cancellationToken);
			Console.WriteLine($"\nOn-chain certificates for this account: {onChain.Count}");
			Console.WriteLine(
				"\nKYC verification complete! The KYC certificate is now attached to your account on-chain.");
			return 0;
		}

		return 0;
	}

	/// <summary>Publish <paramref name="issued"/> with its intermediates recorded as the bundle.</summary>
	private static async Task PublishCertificate(
		WasmRuntime runtime,
		UserClient userClient,
		IssuedCertificate issued,
		CancellationToken cancellationToken)
	{
		List<CryptoCertificate> intermediates = new();
		try
		{
			foreach (string intermediatePem in issued.Intermediates)
			{
				intermediates.Add(runtime.Certificates.Parse(intermediatePem));
			}

			using CryptoCertificate certificate = runtime.Certificates.Parse(issued.Value);
			await userClient.ModifyCertificate(
				AdjustMethod.Add,
				certificate,
				intermediates,
				cancellationToken: cancellationToken).ConfigureAwait(false);
		}
		finally
		{
			foreach (CryptoCertificate intermediate in intermediates)
			{
				intermediate.Dispose();
			}
		}
	}
}
