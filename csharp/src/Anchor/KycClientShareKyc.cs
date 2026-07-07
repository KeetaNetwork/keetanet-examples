using System.Text.Json;
using KeetaNet.Anchor;
using KeetaNet.Anchor.Crypto;
using CryptoCertificate = KeetaNet.Anchor.Crypto.Certificate;
using OnChainCertificate = KeetaNet.Anchor.Certificate;
using KeetaNet.Examples.Common;
using UserClient = KeetaNet.Examples.Network.UserClient;
using KeetaNet.Examples.Anchor.AssetMovement;

namespace KeetaNet.Examples.Anchor;

public sealed class KycClientShareKycExample : IKeetaExample
{
	private const string Network = "test";

	public string Id => "anchor/kyc-client-sharekyc";

	public string Description =>
		"Use the Keeta Anchor Client to share KYC attributes to an Anchor and onboard";

	public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
	{
		Console.WriteLine("""
			Keeta KYC Example: Request USD Bank Deposit Address
			====================================================
			""");

		using var runtime = WasmRuntime.Load();
		string seed = Helper.ReadLine("Enter your Keeta SEED with KYC completed: ").Trim();
		if (seed.Length == 0)
		{
			throw new InvalidOperationException("Invalid seed");
		}

		using Account userAccount = runtime.Accounts.FromSeed(seed, 0, "ecdsa_secp256k1");
		Console.WriteLine($"Keeta Account: {userAccount.Address}\n");

		using UserClient userClient = UserClient.FromNetwork(Network, userAccount);
		using KycClient kycClient = runtime.CreateKycClient(
			Constants.NodeApi,
			userClient.NetworkAddress,
			userAccount);
		using AssetMovementClient assetMovementClient = runtime.CreateAssetMovementClient(
			Constants.NodeApi,
			userClient.NetworkAddress,
			userAccount);

		string keetaDestination = $"chain:keeta:{userClient.Network}";
		var assetPair = new { from = "USD", to = Constants.KeetaUsdAsset };

		IReadOnlyList<AssetProvider> providers = await assetMovementClient.GetProvidersForTransferAsync(
			new AssetProviderSearch(
				Asset: assetPair,
				From: Constants.BankAccountUsLocation,
				To: keetaDestination),
			cancellationToken);

		if (providers.Count == 0)
		{
			throw new InvalidOperationException("No asset movement providers found for USD bank deposit to Keeta");
		}

		AssetProvider provider = providers[0];
		Console.WriteLine($"Using provider: {provider.Id}");

		AssetCreateAddressRequest persistentAddressRequest = new(
			SourceLocation: Constants.BankAccountUsLocation,
			Asset: assetPair,
			DestinationLocation: keetaDestination,
			DestinationAddress: userAccount.Address);

		try
		{
			JsonElement persistentAddress = await CreatePersistentForwardingAddressWithOnboardingAsync(
				runtime,
				assetMovementClient,
				provider,
				userClient,
				persistentAddressRequest,
				promptBeforeOnboarding: true,
				cancellationToken);

			Console.WriteLine("Persistent address created (KYC share was not required):");
			Console.WriteLine(JsonSerializer.Serialize(persistentAddress, new JsonSerializerOptions { WriteIndented = true }));
			return 0;
		}
		catch (KycShareNeededException shareNeeded)
		{
			Console.WriteLine("KYC Share Instructions:");
			Console.WriteLine(JsonSerializer.Serialize(new
			{
				neededAttributes = shareNeeded.Blocker.NeededAttributes,
				shareWithPrincipals = shareNeeded.Blocker.ShareWithPrincipals,
				acceptedIssuers = shareNeeded.Blocker.AcceptedIssuers,
				tosFlow = shareNeeded.Blocker.TosFlow,
			}, new JsonSerializerOptions { WriteIndented = true }));

			string proceed = Helper.ReadLine("\nShare KYC attributes and retry? (y/n): ");
			if (!proceed.Trim().Equals("y", StringComparison.OrdinalIgnoreCase))
			{
				Console.WriteLine("Exiting without sharing KYC attributes.");
				return 0;
			}

			using SharableCertificateAttributes sharable = await BuildSharableKycAttributesAsync(
				runtime,
				kycClient,
				userClient,
				userAccount,
				shareNeeded.Blocker,
				cancellationToken);

			IReadOnlyList<string> sharedAttributeNames = sharable.GetAttributeNames();
			Console.WriteLine(
				$"Sharing {sharedAttributeNames.Count} KYC attributes: {string.Join(", ", sharedAttributeNames)}\n");

			if (shareNeeded.Blocker.TosFlow is { ValueKind: JsonValueKind.Object } tosFlow
				&& tosFlow.TryGetProperty("type", out JsonElement tosType)
				&& tosType.GetString() == "url-flow"
				&& tosFlow.TryGetProperty("url", out JsonElement tosUrl))
			{
				Console.WriteLine($"\nAccept Terms of Service:\n  {tosUrl.GetString()}\n");
				Helper.ReadLine("Press Enter after accepting TOS: ");
			}

			await assetMovementClient.ShareKycAttributesAndWaitAsync(
				provider,
				new AssetShareKycRequest(sharable.ToPem()),
				cancellationToken: cancellationToken);

			Console.WriteLine("KYC attributes shared.\n");

			JsonElement createdAddress = await CreatePersistentForwardingAddressWithOnboardingAsync(
				runtime,
				assetMovementClient,
				provider,
				userClient,
				persistentAddressRequest,
				promptBeforeOnboarding: true,
				cancellationToken);

			Console.WriteLine("Persistent address created:");
			Console.WriteLine(JsonSerializer.Serialize(createdAddress, new JsonSerializerOptions { WriteIndented = true }));
			return 0;
		}
	}

	private static async Task<JsonElement> CreatePersistentForwardingAddressWithOnboardingAsync(
		WasmRuntime runtime,
		AssetMovementClient assetMovementClient,
		AssetProvider provider,
		UserClient userClient,
		AssetCreateAddressRequest request,
		bool promptBeforeOnboarding,
		CancellationToken cancellationToken)
	{
		while (!cancellationToken.IsCancellationRequested)
		{
			try
			{
				return await assetMovementClient.CreatePersistentForwardingAddressAsync(
					provider,
					request,
					cancellationToken);
			}
			catch (KeetaException error) when (error.Code == Blockers.KycShareNeededCode)
			{
				KycShareNeededBlocker? kycShareNeeded = Blockers.TryParseKycShareNeeded(error);
				if (kycShareNeeded is not null)
				{
					throw new KycShareNeededException(kycShareNeeded);
				}

				throw;
			}
			catch (KeetaException error) when (error.Code == Blockers.UserActionNeededCode)
			{
				UserActionNeededBlocker? userActionNeeded = Blockers.TryParseUserActionNeeded(error);
				if (userActionNeeded is null)
				{
					throw;
				}

				Console.WriteLine("Onboarding steps required:");
				Console.WriteLine(JsonSerializer.Serialize(userActionNeeded.ActionsNeeded, new JsonSerializerOptions { WriteIndented = true }));

				if (promptBeforeOnboarding)
				{
					string proceed = Helper.ReadLine("\nComplete onboarding steps and retry? (y/n): ");
					if (!proceed.Trim().Equals("y", StringComparison.OrdinalIgnoreCase))
					{
						throw;
					}
				}

				await UserActions.ExecuteAsync(runtime, userClient, userActionNeeded, cancellationToken);
				Console.WriteLine("Onboarding steps completed.\n");
			}
		}

		throw new OperationCanceledException(cancellationToken);
	}

	private static async Task<SharableCertificateAttributes> BuildSharableKycAttributesAsync(
		WasmRuntime runtime,
		KycClient kycClient,
		UserClient userClient,
		Account userAccount,
		KycShareNeededBlocker blocker,
		CancellationToken cancellationToken)
	{
		(KycCertificate selected, IReadOnlyList<CryptoCertificate> intermediates) = await SelectOnChainKycCertificateAsync(
			runtime,
			kycClient,
			userAccount,
			blocker.RequiresTrustedChain,
			cancellationToken);

		try
		{
			SharableCertificateAttributes sharable = SharableKycBuilder.Build(
				runtime,
				selected,
				userAccount,
				intermediates,
				blocker.NeededAttributes ?? [],
				cancellationToken);

			foreach (string principalAddress in blocker.ShareWithPrincipals)
			{
				using Account principal = runtime.Accounts.FromAccount(principalAddress);
				sharable.GrantAccess([principal]);
			}

			return sharable;
		}
		finally
		{
			selected.Dispose();
			foreach (CryptoCertificate intermediate in intermediates)
			{
				intermediate.Dispose();
			}
		}
	}

	private static async Task<(KycCertificate Certificate, IReadOnlyList<CryptoCertificate> Intermediates)> SelectOnChainKycCertificateAsync(
		WasmRuntime runtime,
		KycClient kycClient,
		Account userAccount,
		bool requireTrustedChain,
		CancellationToken cancellationToken)
	{
		IReadOnlyList<OnChainCertificate> records =
			await kycClient.GetAllCertificatesAsync(userAccount, cancellationToken);
		if (records.Count == 0)
		{
			throw new InvalidOperationException("No on-chain KYC certificates found for this account");
		}

		using CryptoCertificate? trustedRoot = requireTrustedChain
			? runtime.Certificates.Parse(Constants.KycRootCaPem)
			: null;
		List<string> rejections = new();

		foreach (OnChainCertificate record in records)
		{
			List<CryptoCertificate> intermediateCertificates = new();
			foreach (string intermediatePem in record.Intermediates)
			{
				intermediateCertificates.Add(runtime.Certificates.Parse(intermediatePem));
			}

			KycCertificate certificate = runtime.KycCertificates.Parse(record.Value);
			try
			{
				if (requireTrustedChain && trustedRoot is not null)
				{
					if (!certificate.Verify(
						new[] { trustedRoot },
						intermediateCertificates,
						DateTimeOffset.UtcNow))
					{
						using CryptoCertificate baseCertificate = certificate.Base();
						rejections.Add($"chain not trusted (issuer DN: {baseCertificate.Issuer})");
						certificate.Dispose();
						foreach (CryptoCertificate intermediate in intermediateCertificates)
						{
							intermediate.Dispose();
						}

						continue;
					}
				}
				else if (!certificate.IsValidAt(DateTimeOffset.UtcNow))
				{
					rejections.Add("certificate not valid at current time");
					certificate.Dispose();
					foreach (CryptoCertificate intermediate in intermediateCertificates)
					{
						intermediate.Dispose();
					}

					continue;
				}

				return (certificate, intermediateCertificates);
			}
			catch (Exception error)
			{
				rejections.Add(error.Message);
				certificate.Dispose();
				foreach (CryptoCertificate intermediate in intermediateCertificates)
				{
					intermediate.Dispose();
				}
			}
		}

		string message = requireTrustedChain
			? "No on-chain KYC certificate chains to the test-network KYC root CA"
			: "No valid on-chain KYC certificate found";
		throw new InvalidOperationException(
			string.Join('\n', new List<string> { message }.Concat(rejections.Select(reason => $"  - {reason}"))));
	}

	private sealed class KycShareNeededException(KycShareNeededBlocker blocker) : Exception("KYC share is required")
	{
		public KycShareNeededBlocker Blocker { get; } = blocker;
	}
}
