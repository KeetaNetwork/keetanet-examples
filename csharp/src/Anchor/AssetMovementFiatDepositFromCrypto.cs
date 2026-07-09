using System.Text.Json;
using KeetaNet.Anchor;
using KeetaNet.Anchor.Crypto;
using KeetaNet.Examples.Common;
using KeetaNet.Examples.Network;

namespace KeetaNet.Examples.Anchor;

public sealed class AssetMovementFiatDepositFromCryptoExample : IKeetaExample
{
	private const string Network = "test";

	private const string DefaultPassphrase =
		"bottom alley wash elbow devote believe maximum amount camera way direct globe " +
		"frost bottom tilt title ship purse always fluid tennis spread lazy track";

	public string Id => "anchor/asset-movement-fiat-deposit-from-crypto";

	public string Description =>
		"Move USDC from Arbitrum Sepolia to USD on Keeta Test Network via a persistent forwarding address";

	public async Task<int> Run(string[] args, CancellationToken cancellationToken = default)
	{
		Console.WriteLine("Keeta Asset Movement Example: Arbitrum USDC => Keeta USD");

		using var runtime = WasmRuntime.Load();
		string seedInput = Helper.ReadLine(
			"Enter your Keeta SEED with KYC completed (or press Enter for a default seed): ").Trim();

		using Account userAccount = seedInput.Length > 0
			? runtime.Accounts.FromSeed(seedInput, 0, "ecdsa_secp256k1")
			: runtime.Accounts.FromPassphrase(
				DefaultPassphrase.Split(' ', StringSplitOptions.RemoveEmptyEntries),
				0,
				"ecdsa_secp256k1");

		Console.WriteLine($"Keeta Account: {userAccount.PublicKeyString}\n");

		UserClient userClient = UserClient.FromNetwork(Network, userAccount);
		using AssetMovementClient assetMovementClient = runtime.CreateAssetMovementClient(
			Constants.NodeApi,
			userClient.NetworkAddress,
			userAccount);
		using NodeClient nodeClient = runtime.CreateNodeClient(Constants.NodeApi);

		Console.WriteLine("Generating persistent forwarding address please wait...");

		string keetaDestination = $"chain:keeta:{userClient.Network}";
		AssetOrPair assetPair = AssetOrPair.Pair(Constants.ArbitrumUsdcAsset, Constants.KeetaUsdAsset);

		IReadOnlyList<AssetProvider> providers = await assetMovementClient.GetProvidersForTransfer(
			new AssetProviderSearch(
				Asset: assetPair,
				From: Constants.ArbitrumSepoliaLocation,
				To: keetaDestination),
			cancellationToken);

		if (providers.Count == 0)
		{
			throw new InvalidOperationException(
				"No Asset Movement providers found. This example requires an Asset Movement anchor to be configured.");
		}

		AssetProvider provider = providers[0];
		Console.WriteLine($"Using provider: {provider.Id}");

		JsonElement persistentAddressResponse = await assetMovementClient.CreatePersistentForwardingAddress(
			provider,
			new AssetCreateAddressRequest(
				SourceLocation: Constants.ArbitrumSepoliaLocation,
				Asset: assetPair,
				DestinationLocation: keetaDestination,
				DestinationAddress: userAccount.PublicKeyString),
			cancellationToken);

		string persistentAddress = persistentAddressResponse.GetProperty("address").GetString()
			?? throw new InvalidOperationException("Failed to create persistent forwarding address");

		Console.WriteLine($"""

			========================================
			 YOUR ARBITRUM SEPOLIA FORWARDING ADDRESS
			========================================
			Persistent Address: {persistentAddress}
			This address will automatically forward USDC received on Arbitrum Sepolia
			to USD in your Keeta account: {userAccount.PublicKeyString}
			========================================

			HOW TO GET TEST USDC:
			----------------------------------------
			1. Visit Circle's Testnet Faucet:
			   https://faucet.circle.com/

			2. Select "Arbitrum Sepolia" from the network dropdown

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
									Constants.ArbitrumSepoliaLocation,
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
						balances.Select(entry => new { token = entry.Token.PublicKeyString, balance = entry.Balance.ToString() }),
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
