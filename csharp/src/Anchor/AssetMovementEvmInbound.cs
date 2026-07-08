using System.Text.Json;
using KeetaNet.Anchor;
using KeetaNet.Anchor.Crypto;
using KeetaNet.Examples.Common;
using KeetaNet.Examples.Network;

namespace KeetaNet.Examples.Anchor;

public sealed class AssetMovementEvmInboundExample : IKeetaExample
{
	private const string Network = "test";

	public string Id => "anchor/asset-movement-evm-inbound";

	public string Description =>
		"Example of using the Keeta Anchor Client to move USDC from Base Sepolia to Keeta Network";

	public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
	{
		Console.WriteLine("Keeta Asset Movement Example: Base Sepolia USDC => Keeta");

		using var runtime = WasmRuntime.Load();
		string seed = runtime.Accounts.GenerateRandomSeed();
		using Account userAccount = runtime.Accounts.FromSeed(seed, 0, "ecdsa_secp256k1");

		Console.WriteLine($"Seed: {seed}");
		Console.WriteLine($"Keeta Account: {userAccount.Address}\n");

		using UserClient userClient = UserClient.FromNetwork(Network, userAccount);

		using AssetMovementClient assetMovementClient = runtime.CreateAssetMovementClient(
			Constants.NodeApi,
			userClient.NetworkAddress,
			userAccount);
		using NodeClient nodeClient = runtime.CreateNodeClient(Constants.NodeApi);

		string keetaDestination = $"chain:keeta:{userClient.Network}";

		IReadOnlyList<AssetProvider> providers = await assetMovementClient.GetProvidersForTransfer(
			new AssetProviderSearch(
				Constants.KeetaUsdcAsset,
				Constants.BaseSepoliaLocation,
				keetaDestination),
			cancellationToken);

		if (providers.Count == 0)
		{
			throw new InvalidOperationException(
				"No Asset Movement providers found. This example requires an Asset Movement anchor to be configured.");
		}

		AssetProvider? provider = providers.FirstOrDefault(p => p.Id == Constants.Dev2ProviderId);
		if (provider is null)
		{
			throw new InvalidOperationException("Provider is undefined");
		}

		JsonElement persistentAddressResponse = await assetMovementClient.CreatePersistentForwardingAddress(
			provider,
			new AssetCreateAddressRequest(
				SourceLocation: Constants.BaseSepoliaLocation,
				Asset: Constants.KeetaUsdcAsset,
				DestinationLocation: keetaDestination,
				DestinationAddress: userAccount.Address),
			cancellationToken);

		string persistentAddress = persistentAddressResponse.GetProperty("address").GetString()
			?? throw new InvalidOperationException("Failed to create persistent forwarding address");

		Console.WriteLine($"""
			
			========================================
			 YOUR BASE SEPOLIA FORWARDING ADDRESS
			========================================
			Persistent Address: {persistentAddress}
			This address will automatically forward USDC received on Base Sepolia
			to your Keeta account: {userAccount.Address}
			========================================

			HOW TO GET TEST USDC:
			----------------------------------------
			1. Visit Circle's Testnet Faucet:
			   https://faucet.circle.com/

			2. Select "Base Sepolia" from the network dropdown

			3. Select "USDC" as the token

			4. Enter your forwarding address:
			   {persistentAddress}

			5. Request test USDC (usually 20 USDC per request)
			----------------------------------------
			""");

		string shouldMonitor = Helper.ReadLine("Would you like to monitor for incoming transactions? (yes/no): ");

		if (new[] { "yes", "y" }.Contains(shouldMonitor.ToLowerInvariant()))
		{
			using var stopSignal = new CancellationTokenSource();
			Console.CancelKeyPress += (_, eventArgs) =>
			{
				eventArgs.Cancel = true;
				stopSignal.Cancel();
			};

			Console.WriteLine("Monitoring for transactions... (This will check every 5 seconds. Press Ctrl+C to stop)");

			using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stopSignal.Token);
			CancellationToken monitorToken = linked.Token;

			while (!monitorToken.IsCancellationRequested)
			{
				try
				{
					await Task.Delay(TimeSpan.FromSeconds(5), monitorToken);

					AssetTransactionPage transactionResponse = await assetMovementClient.ListTransactions(
						provider,
						new AssetListTransactionsRequest(
							PersistentAddresses:
							[
								new AssetPersistentAddressFilter(
									Constants.BaseSepoliaLocation,
									persistentAddress),
							]),
						monitorToken);

					if (transactionResponse.Transactions.Count == 0)
					{
						Console.Write('.');
						continue;
					}

					JsonElement tx = transactionResponse.Transactions[0];
					string? status = tx.TryGetProperty("status", out JsonElement statusElement)
						? statusElement.GetString()
						: null;

					if (!string.Equals(status, "COMPLETE", StringComparison.Ordinal))
					{
						continue;
					}

					Console.WriteLine($"""

						Completed transaction detected!
						 ID: {GetString(tx, "id")}
						 Status: {GetString(tx, "status")}
						 Asset: {GetString(tx, "asset")}
						 From: {GetString(tx.GetProperty("from"), "location")}
						 From Value: {GetString(tx.GetProperty("from"), "value")}
						 To: {GetString(tx.GetProperty("to"), "location")}
						 To Value: {GetString(tx.GetProperty("to"), "value")}
						 Created: {GetString(tx, "createdAt")}
						 Updated: {GetString(tx, "updatedAt")}
						""");

					IReadOnlyList<TokenBalance> balances = await nodeClient.GetAccountBalances(userAccount, monitorToken);
					Console.WriteLine("Current Keeta Balances:");
					Console.WriteLine(JsonSerializer.Serialize(
						balances.Select(entry => new { token = entry.Token.Address, balance = entry.Balance.ToString() }),
						new JsonSerializerOptions { WriteIndented = true }));

					Console.WriteLine("Transaction completed successfully. Exiting...");
					return 0;
				}
				catch (OperationCanceledException) when (monitorToken.IsCancellationRequested)
				{
					break;
				}
				catch (Exception error)
				{
					Console.Error.WriteLine($"Error monitoring transactions: {error}");
				}
			}

			Console.WriteLine("Monitoring stopped.");
		}
		else
		{
			Console.WriteLine("Example completed!");
		}

		return 0;
	}

	private static string GetString(JsonElement element, string name) =>
		element.TryGetProperty(name, out JsonElement value) ? value.ToString() : string.Empty;
}
