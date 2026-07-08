using System.Globalization;
using System.Numerics;
using System.Text.Json;
using KeetaNet.Anchor;
using KeetaNet.Anchor.Crypto;
using KeetaNet.Examples.Common;
using UserClient = KeetaNet.Examples.Network.UserClient;

namespace KeetaNet.Examples.Anchor;

public sealed class AssetMovementEvmOutboundExample : IKeetaExample
{
	private const string Network = "test";
	private const int UsdcDecimals = 6;

	public string Id => "anchor/asset-movement-evm-outbound";

	public string Description =>
		"Example of using the Keeta Anchor Client to move USDC from Keeta Test Network to Base Sepolia";

	public async Task<int> Run(string[] args, CancellationToken cancellationToken = default)
	{
		Console.WriteLine("""
			Keeta Asset Movement Example: Keeta => Base Sepolia USDC
			=========================================================
			IMPORTANT: Before running this example:
			1. Run asset-movement-evm-inbound.ts to receive USDC tokens on Keeta Test Network
			2. Ensure you have sufficient USDC balance on Keeta to send
			""");

		using var runtime = WasmRuntime.Load();
		string seedInput = Helper.ReadLine("Enter your Keeta SEED (or press Enter for new random seed): ").Trim();
		string seed = seedInput.Length == 0 ? runtime.Accounts.GenerateRandomSeed() : seedInput;
		using Account userAccount = runtime.Accounts.FromSeed(seed, 0, "ecdsa_secp256k1");

		Console.WriteLine($"Keeta Account: {userAccount.Address}");

		string baseRecipientAddress = Helper.ReadLine("Enter the Base Sepolia wallet address to send USDC to: ").Trim();
		if (baseRecipientAddress.Length != 42 || !baseRecipientAddress.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException("Invalid Base Sepolia address. Must be a valid Ethereum address (0x...)");
		}

		using UserClient userClient = UserClient.FromNetwork(Network, userAccount);
		using NodeClient nodeClient = runtime.CreateNodeClient(Constants.NodeApi);

		using Account baseToken = runtime.Accounts.FromAccount(userClient.BaseToken);
		BigInteger baseTokenBalance = await nodeClient.GetAccountBalance(userAccount, baseToken, cancellationToken);
		if (baseTokenBalance == BigInteger.Zero)
		{
			if (!await Helper.GetFaucetTokens(runtime, userAccount, Network, cancellationToken))
			{
				throw new InvalidOperationException("Failed to get Faucet Tokens");
			}
		}

		using Account usdcToken = runtime.Accounts.FromAccount(Constants.KeetaUsdcAsset);
		BigInteger currentBalance = await nodeClient.GetAccountBalance(userAccount, usdcToken, cancellationToken);
		Console.WriteLine($"\nCurrent USDC Balance: {currentBalance} ({Helper.FormatDecimals(currentBalance, UsdcDecimals)} USDC)");

		if (currentBalance == BigInteger.Zero)
		{
			throw new InvalidOperationException(
				"You have no USDC balance on Keeta Test Network. Please run asset-movement-evm-inbound.ts first to get USDC tokens.");
		}

		string amountInput = Helper.ReadLine(
			$"How much USDC do you want to send? (in USDC, max {Helper.FormatDecimals(currentBalance, UsdcDecimals)}): ").Trim();
		if (!decimal.TryParse(amountInput, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal amountInUsdc)
			|| amountInUsdc <= 0)
		{
			throw new InvalidOperationException("Invalid amount. Please enter a positive number.");
		}

		BigInteger amountToSend = new((long)decimal.Truncate(amountInUsdc * 1_000_000m));
		if (amountToSend > currentBalance)
		{
			throw new InvalidOperationException(
				$"Insufficient balance. You only have {Helper.FormatDecimals(currentBalance, UsdcDecimals)} USDC");
		}

		using AssetMovementClient assetMovementClient = runtime.CreateAssetMovementClient(
			Constants.NodeApi,
			userClient.NetworkAddress,
			userAccount);

		string keetaSource = $"chain:keeta:{userClient.Network}";

		IReadOnlyList<AssetProvider> providers = await assetMovementClient.GetProvidersForTransfer(
			new AssetProviderSearch(
				Constants.KeetaUsdcAsset,
				keetaSource,
				Constants.BaseSepoliaLocation),
			cancellationToken);

		if (providers.Count == 0)
		{
			throw new InvalidOperationException(
				"No Asset Movement providers found for Keeta => Base Sepolia. Please ensure an Asset Movement anchor is configured to support this transfer.");
		}

		AssetProvider? provider = providers.FirstOrDefault(p => p.Id == Constants.Dev2ProviderId);
		if (provider is null)
		{
			throw new InvalidOperationException("Provider is undefined");
		}

		AssetTransfer transfer = await assetMovementClient.InitiateTransfer(
			provider,
			new AssetTransferRequest(
				Constants.KeetaUsdcAsset,
				new AssetTransferSource(keetaSource),
				new AssetTransferDestination(Constants.BaseSepoliaLocation, baseRecipientAddress),
				amountToSend.ToString(CultureInfo.InvariantCulture)),
			cancellationToken);

		Console.WriteLine($"\nTransfer initiated with ID: {transfer.Id}");
		Console.WriteLine(JsonSerializer.Serialize(transfer.InstructionChoices, new JsonSerializerOptions { WriteIndented = true }));

		JsonElement instruction = transfer.InstructionChoices[0];
		if (instruction.GetProperty("type").GetString() != "KEETA_SEND")
		{
			throw new InvalidOperationException("Expected KEETA_SEND instruction not found");
		}

		string anchorAccount = instruction.GetProperty("sendToAddress").GetString()
			?? throw new InvalidOperationException("Expected external field data in instruction");

		if (!instruction.TryGetProperty("external", out JsonElement externalElement))
		{
			throw new InvalidOperationException("Expected external field data in instruction");
		}

		string external = externalElement.ValueKind == JsonValueKind.String
			? externalElement.GetString()!
			: externalElement.GetRawText();

		using Account sendTo = runtime.Accounts.FromAccount(anchorAccount);
		await userClient.Send(runtime, sendTo, amountToSend, usdcToken, external, cancellationToken);

		Console.WriteLine("\nMonitoring transfer status... (This will check every 5 seconds. Press Ctrl+C to stop)");

		using var stopSignal = new CancellationTokenSource();
		Console.CancelKeyPress += (_, eventArgs) =>
		{
			eventArgs.Cancel = true;
			stopSignal.Cancel();
		};

		using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stopSignal.Token);
		CancellationToken monitorToken = linked.Token;
		DateTimeOffset startTime = DateTimeOffset.UtcNow;

		while (!monitorToken.IsCancellationRequested)
		{
			try
			{
				await Task.Delay(TimeSpan.FromSeconds(5), monitorToken);

				AssetTransferStatus transactionResult = await transfer.GetTransferStatus(monitorToken);
				string? status = transactionResult.Transaction.TryGetProperty("status", out JsonElement statusElement)
					? statusElement.GetString()
					: null;

				int elapsed = (int)(DateTimeOffset.UtcNow - startTime).TotalSeconds;
				Console.WriteLine($"\n[{elapsed}s] Status: {status ?? transactionResult.Transaction.GetRawText()}");

				if (string.Equals(status, "COMPLETE", StringComparison.Ordinal))
				{
					Console.WriteLine($"""

						========================================
						  TRANSFER COMPLETED SUCCESSFULLY!
						========================================
						Transfer ID: {transfer.Id}
						Amount: {Helper.FormatDecimals(amountToSend, UsdcDecimals)} USDC
						From: Keeta Test Network
						To: Base Sepolia ({baseRecipientAddress})
						========================================
						""");

					BigInteger finalBalance = await nodeClient.GetAccountBalance(userAccount, usdcToken, monitorToken);
					Console.WriteLine($"Final USDC Balance on Keeta: {Helper.FormatDecimals(finalBalance, UsdcDecimals)} USDC");
					return 0;
				}

				Console.Write('.');
			}
			catch (OperationCanceledException) when (monitorToken.IsCancellationRequested)
			{
				break;
			}
			catch (Exception error)
			{
				Console.Error.WriteLine($"\nError monitoring transfer: {error}");
			}
		}

		Console.WriteLine("\nMonitoring stopped.");
		Console.WriteLine($"Transfer ID: {transfer.Id}");
		return 0;
	}
}
