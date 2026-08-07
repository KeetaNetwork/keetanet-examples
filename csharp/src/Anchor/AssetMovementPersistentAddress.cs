using System.Text.Json;
using KeetaNet.Anchor;
using KeetaNet.Anchor.Crypto;
using KeetaNet.Examples.Common;

namespace KeetaNet.Examples.Anchor;

public sealed class AssetMovementPersistentAddressExample : IKeetaExample
{
	private const KeetaNetwork Network = KeetaNetwork.Test;

	public string Id => "anchor/asset-movement-persistent-address";

	public string Description =>
		"Example of using the Keeta Anchor Client to create a persistent forwarding address";

	public async Task<int> Run(string[] args, CancellationToken cancellationToken = default)
	{
		using var runtime = WasmRuntime.Load();

		// Generate a random account for this demo.
		string seed = runtime.Accounts.GenerateRandomSeed();
		using Account userAccount = runtime.Accounts.FromSeed(seed, 0, "ecdsa_secp256k1");

		Console.WriteLine($"Seed: {seed}");
		Console.WriteLine($"Keeta Account: {userAccount.PublicKeyString}");

		// Create an Asset Movement client to handle cross-chain transfers.
		using AssetMovementClient assetMovementClient = runtime.CreateAssetMovementClient(
			Network.RepresentativeApiUrl(),
			Constants.MetadataRoot,
			userAccount);

		string keetaDestination = $"chain:keeta:{Network.Id()}";

		// Step 1: Identify a provider that forwards USDC from Base Sepolia to
		// the Keeta network.
		IReadOnlyList<AssetProvider> providers = await assetMovementClient.GetProvidersForTransfer(
			new AssetProviderSearch(
				Constants.KeetaUsdcAsset,
				Constants.BaseSepoliaLocation,
				keetaDestination),
			cancellationToken);

		if (providers.Count == 0)
		{
			throw new InvalidOperationException("No Providers found");
		}

		// Use the DEV2 provider, which does not require KYC.
		AssetProvider? provider = providers.FirstOrDefault(p => p.Id == Constants.Dev2ProviderId);
		if (provider is null)
		{
			throw new InvalidOperationException("Provider is undefined");
		}

		// Step 2: Create a persistent forwarding address on Base Sepolia that
		// automatically forwards received USDC to the Keeta account.
		AssetForwardingAddress created = await provider.CreatePersistentForwardingAddress(
			new AssetCreateAddressRequest(
				SourceLocation: Constants.BaseSepoliaLocation,
				Asset: Constants.KeetaUsdcAsset,
				DestinationLocation: keetaDestination,
				DestinationAddress: userAccount.PublicKeyString),
			cancellationToken);

		string persistentAddress = created.Address.ValueKind == JsonValueKind.String
			? created.Address.GetString()!
			: throw new InvalidOperationException("Failed to create persistent forwarding address");

		Console.WriteLine($"Persistent address: {persistentAddress}");
		Console.WriteLine($"Forward to Keeta account: {userAccount.PublicKeyString}");

		// The address can now be shared. USDC received on Base Sepolia is
		// automatically forwarded to the Keeta account.
		return 0;
	}
}
