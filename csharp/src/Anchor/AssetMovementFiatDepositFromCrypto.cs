using System.Text.Json;
using KeetaNet.Anchor;
using KeetaNet.Anchor.Crypto;
using KeetaNet.Examples.Common;

namespace KeetaNet.Examples.Anchor;

public sealed class AssetMovementFiatDepositFromCryptoExample : IKeetaExample
{
	private const KeetaNetwork Network = KeetaNetwork.Test;

	private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

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

		using UserClient userClient = runtime.CreateUserClient(Network, userAccount);
		using AssetMovementClient assetMovementClient = runtime.CreateAssetMovementClient(
			Network.RepresentativeApiUrl(),
			Constants.MetadataRoot,
			userAccount);

		Console.WriteLine("Generating persistent forwarding address please wait...");

		string keetaDestination = $"chain:keeta:{Network.Id()}";
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

		string persistentAddress;
		try
		{
			AssetForwardingAddress created = await provider.CreatePersistentForwardingAddress(
				new AssetCreateAddressRequest(
					SourceLocation: Constants.ArbitrumSepoliaLocation,
					Asset: assetPair,
					DestinationLocation: keetaDestination,
					DestinationAddress: userAccount.PublicKeyString),
				cancellationToken);

			persistentAddress = created.Address.ValueKind == JsonValueKind.String
				? created.Address.GetString()!
				: throw new InvalidOperationException("Failed to create persistent forwarding address");
		}
		catch (KeetaBlockerException refusal)
		{
			switch (refusal.Blocker)
			{
				case AssetKycShareNeededBlocker:
					Console.Error.WriteLine(
						"KYC attributes must be shared with the provider before an Arbitrum USDC forwarding address can be created.");
					Console.Error.WriteLine("Complete KYC sharing (see anchor/kyc-client-sharekyc), then run this example again.");
					return 0;
				case AssetUserActionNeededBlocker userActionNeeded:
					Console.Error.WriteLine(
						"Provider onboarding steps are still required before an Arbitrum USDC forwarding address can be created.");
					Console.Error.WriteLine("Complete the actions below (see anchor/kyc-client-sharekyc), then run this example again.");
					Console.Error.WriteLine(JsonSerializer.Serialize(userActionNeeded.ActionsNeeded, JsonOptions));
					return 0;
				default:
					throw;
			}
		}
		catch (KeetaException error) when (error.Code == "SERVICE")
		{
			Console.Error.WriteLine("The provider rejected the forwarding address request.");
			Console.Error.WriteLine(error.Message);
			return 1;
		}

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

					AssetTransactionPage transactionResponse = await provider.ListTransactions(
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

					IReadOnlyList<TokenBalance> balances = await userClient.GetAllBalances(monitorToken);
					Console.WriteLine("Current Keeta Balances:");
					Console.WriteLine(JsonSerializer.Serialize(
						balances.Select(entry => new { token = entry.Token.PublicKeyString, balance = entry.Balance.ToString() }),
						JsonOptions));

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
