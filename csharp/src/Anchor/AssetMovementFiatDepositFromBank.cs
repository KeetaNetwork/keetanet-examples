using System.Text.Json;
using KeetaNet.Anchor;
using KeetaNet.Anchor.Crypto;
using KeetaNet.Examples.Common;

namespace KeetaNet.Examples.Anchor;

/// <summary>
/// Port of <c>src/anchor/asset-movement-fiat-deposit-from-bank.ts</c>.
/// Assumes KYC is complete and provider onboarding has already been performed.
/// </summary>
public sealed class AssetMovementFiatDepositFromBankExample : IKeetaExample
{
	private const KeetaNetwork Network = KeetaNetwork.Test;

	private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

	public string Id => "anchor/asset-movement-fiat-deposit-from-bank";

	public string Description =>
		"Request USD bank deposit information (persistent address) for USD on Keeta";

	public async Task<int> Run(string[] args, CancellationToken cancellationToken = default)
	{
		Console.WriteLine("Keeta Fiat Deposit Example: USD Bank Deposit Information");
		Console.WriteLine("==========================================================");
		Console.WriteLine();

		using var runtime = WasmRuntime.Load();
		string seed = Helper.ReadLine("Enter your Keeta SEED with KYC completed: ").Trim();
		if (seed.Length == 0)
		{
			throw new InvalidOperationException("Invalid seed");
		}

		using Account userAccount = runtime.Accounts.FromSeed(seed, 0, "ecdsa_secp256k1");
		Console.WriteLine($"Keeta Account: {userAccount.PublicKeyString}");
		Console.WriteLine($"USD Token: {Constants.KeetaUsdAsset}");
		Console.WriteLine();

		using AssetMovementClient assetMovementClient = runtime.CreateAssetMovementClient(
			Network.RepresentativeApiUrl(),
			Constants.MetadataRoot,
			userAccount);

		string keetaDestination = $"chain:keeta:{Network.Id()}";
		AssetOrPair assetPair = AssetOrPair.Pair("USD", Constants.KeetaUsdAsset);

		IReadOnlyList<AssetProvider> providers = await assetMovementClient.GetProvidersForTransfer(
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
		Console.WriteLine();

		AssetCreateAddressRequest request = new(
			SourceLocation: Constants.BankAccountUsLocation,
			Asset: assetPair,
			DestinationLocation: keetaDestination,
			DestinationAddress: userAccount.PublicKeyString);

		try
		{
			AssetForwardingAddress depositInfo = await provider.CreatePersistentForwardingAddress(
				request,
				cancellationToken);

			Console.WriteLine("USD bank deposit information:");
			Console.WriteLine(JsonSerializer.Serialize(depositInfo, JsonOptions));
			return 0;
		}
		catch (KeetaBlockerException refusal)
		{
			switch (refusal.Blocker)
			{
				case AssetKycShareNeededBlocker:
					Console.Error.WriteLine("KYC attributes must be shared with the provider before a USD deposit address can be issued.");
					Console.Error.WriteLine("Complete KYC sharing (see anchor/kyc-client-sharekyc), then run this example again.");
					return 0;
				case AssetUserActionNeededBlocker userActionNeeded:
					Console.Error.WriteLine("Provider onboarding steps are still required before a USD deposit address can be issued.");
					Console.Error.WriteLine("Complete the actions below (see anchor/kyc-client-sharekyc), then run this example again.");
					Console.Error.WriteLine(JsonSerializer.Serialize(userActionNeeded.ActionsNeeded, JsonOptions));
					return 0;
				default:
					throw;
			}
		}
	}
}
