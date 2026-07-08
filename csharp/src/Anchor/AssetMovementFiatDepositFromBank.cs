using System.Text.Json;
using KeetaNet.Anchor;
using KeetaNet.Anchor.Crypto;
using KeetaNet.Examples.Anchor.AssetMovement;
using KeetaNet.Examples.Common;
using UserClient = KeetaNet.Examples.Network.UserClient;

namespace KeetaNet.Examples.Anchor;

/// <summary>
/// Port of <c>src/anchor/asset-movement-fiat-deposit-from-bank.ts</c>.
/// Assumes KYC is complete and provider onboarding has already been performed.
/// </summary>
public sealed class AssetMovementFiatDepositFromBankExample : IKeetaExample
{
	private const string Network = "test";

	private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

	public string Id => "anchor/asset-movement-fiat-deposit-from-bank";

	public string Description =>
		"Request USD bank deposit information (persistent address) for USD on Keeta";

	public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
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
		Console.WriteLine($"Keeta Account: {userAccount.Address}");
		Console.WriteLine($"USD Token: {Constants.KeetaUsdAsset}");
		Console.WriteLine();

		using UserClient userClient = UserClient.FromNetwork(Network, userAccount);
		using AssetMovementClient assetMovementClient = runtime.CreateAssetMovementClient(
			Constants.NodeApi,
			userClient.NetworkAddress,
			userAccount);

		string keetaDestination = $"chain:keeta:{userClient.Network}";
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

		AssetAccountStatus accountStatus = await assetMovementClient.GetAccountStatus(provider, cancellationToken);
		if (!AccountStatusReport.IsReady(accountStatus, "a USD deposit address can be issued"))
		{
			return 0;
		}

		AssetCreateAddressRequest request = new(
			SourceLocation: Constants.BankAccountUsLocation,
			Asset: assetPair,
			DestinationLocation: keetaDestination,
			DestinationAddress: userAccount.Address);

		JsonElement depositInfo = await assetMovementClient.CreatePersistentForwardingAddress(
			provider,
			request,
			cancellationToken);

		Console.WriteLine("USD bank deposit information:");
		Console.WriteLine(JsonSerializer.Serialize(depositInfo, JsonOptions));
		return 0;
	}
}
