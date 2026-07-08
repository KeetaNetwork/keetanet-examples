using System.Globalization;
using System.Numerics;
using System.Text.Json;
using KeetaNet.Anchor;
using KeetaNet.Anchor.Crypto;
using KeetaNet.Examples.Anchor.AssetMovement;
using KeetaNet.Examples.Common;
using UserClient = KeetaNet.Examples.Network.UserClient;

namespace KeetaNet.Examples.Anchor;

/// <summary>
/// Port of <c>src/anchor/asset-movement-fiat-withdraw-to-bank.ts</c>.
/// Assumes KYC is complete and provider onboarding has already been performed.
/// </summary>
public sealed class AssetMovementFiatWithdrawToBankExample : IKeetaExample
{
	private const string Network = "test";

	private static readonly HashSet<string> UsStateCodes = new(StringComparer.OrdinalIgnoreCase)
	{
		"AL", "AK", "AZ", "AR", "CA", "CO", "CT", "DE", "DC", "FL", "GA", "HI", "ID", "IL", "IN", "IA", "KS", "KY",
		"LA", "ME", "MD", "MA", "MI", "MN", "MS", "MO", "MT", "NE", "NV", "NH", "NJ", "NM", "NY", "NC", "ND", "OH",
		"OK", "OR", "PA", "RI", "SC", "SD", "TN", "TX", "UT", "VT", "VA", "WA", "WV", "WI", "WY",
	};

	private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

	public string Id => "anchor/asset-movement-fiat-withdraw-to-bank";

	public string Description =>
		"Withdraw USD from Keeta Test Network to a US bank account";

	public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
	{
		Console.WriteLine("Keeta Fiat Withdraw Example: Keeta USD => US Bank");
		Console.WriteLine("===================================================");
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

		int? usdDecimals = await Helper.GetTokenDecimalsAsync(Network, Constants.KeetaUsdAsset, cancellationToken);
		if (usdDecimals is null)
		{
			throw new InvalidOperationException("Failed to get USD token decimals");
		}

		BigInteger baseTokenBalance = await userClient.BalanceAsync(userClient.BaseToken, cancellationToken);
		if (baseTokenBalance == BigInteger.Zero)
		{
			if (!await Helper.GetFaucetTokensAsync(userAccount, Network, cancellationToken))
			{
				throw new InvalidOperationException("Failed to get faucet tokens for transaction fees");
			}
		}

		using Account usdToken = runtime.Accounts.FromAccount(Constants.KeetaUsdAsset);
		BigInteger currentBalance = await userClient.BalanceAsync(usdToken, cancellationToken);
		Console.WriteLine($"Current USD Balance: {Helper.FormatDecimals(currentBalance, usdDecimals.Value)} USD");

		if (currentBalance == BigInteger.Zero)
		{
			throw new InvalidOperationException(
				"You have no USD balance on Keeta Test Network. Deposit USD first (see asset-movement-fiat-deposit-from-bank).");
		}

		string amountInput = Helper.ReadLine(
			$"How much USD do you want to withdraw? (max {Helper.FormatDecimals(currentBalance, usdDecimals.Value)}): ").Trim();
		if (!decimal.TryParse(amountInput, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal amountInUsd)
			|| amountInUsd <= 0)
		{
			throw new InvalidOperationException("Invalid amount. Enter a positive number.");
		}

		decimal multiplier = (decimal)Math.Pow(10, usdDecimals.Value);
		BigInteger amountToWithdraw = new BigInteger(decimal.Truncate(amountInUsd * multiplier));
		if (amountToWithdraw > currentBalance)
		{
			throw new InvalidOperationException(
				$"Insufficient balance. You only have {Helper.FormatDecimals(currentBalance, usdDecimals.Value)} USD");
		}

		string accountNumber = Helper.ReadLine("Enter the US bank account number: ").Trim();
		string routingNumber = Helper.ReadLine("Enter the US bank routing number: ").Trim();
		string bankName = Helper.ReadLine("Enter the US bank name: ").Trim();
		string accountType = Helper.ReadLine("Enter the account type (checking or savings): ").Trim().ToLowerInvariant();
		string firstName = Helper.ReadLine("Enter the account holder first name: ").Trim();
		string lastName = Helper.ReadLine("Enter the account holder last name: ").Trim();
		string addressLine1 = Helper.ReadLine("Enter the US account holder address line 1: ").Trim();
		string addressLine2 = Helper.ReadLine("Enter the US account holder address line 2 (optional): ").Trim();
		string city = Helper.ReadLine("Enter the account holder city: ").Trim();
		string subdivision = Helper.ReadLine("Enter the account holder state (2-letter code): ").Trim();
		string postalCode = Helper.ReadLine("Enter the account holder postal code: ").Trim();

		if (routingNumber.Length == 0 || accountNumber.Length == 0 || bankName.Length == 0 || accountType.Length == 0
			|| firstName.Length == 0 || lastName.Length == 0 || addressLine1.Length == 0 || city.Length == 0
			|| subdivision.Length == 0 || postalCode.Length == 0)
		{
			throw new InvalidOperationException("All bank and account holder fields are required");
		}

		ValidateUsBankDetails(routingNumber, accountType, subdivision);

		subdivision = subdivision.ToUpperInvariant();

		var bankRecipient = new
		{
			type = "bank-account",
			accountType = "us",
			accountNumber,
			routingNumber,
			bankName,
			accountTypeDetail = accountType,
			accountOwner = new
			{
				type = "individual",
				firstName,
				lastName,
			},
			accountAddress = new
			{
				line1 = addressLine1,
				line2 = addressLine2,
				city,
				subdivision,
				postalCode,
				country = "US",
			},
		};

		using AssetMovementClient assetMovementClient = runtime.CreateAssetMovementClient(
			Constants.NodeApi,
			userClient.NetworkAddress,
			userAccount);

		string keetaSource = $"chain:keeta:{userClient.Network}";
		var assetPair = new { from = Constants.KeetaUsdAsset, to = "USD" };

		IReadOnlyList<AssetProvider> providers = await assetMovementClient.GetProvidersForTransferAsync(
			new AssetProviderSearch(
				Asset: assetPair,
				From: keetaSource,
				To: Constants.BankAccountUsLocation),
			cancellationToken);

		if (providers.Count == 0)
		{
			throw new InvalidOperationException("No asset movement providers found for Keeta USD withdrawal to US bank");
		}

		AssetProvider provider = providers[0];
		Console.WriteLine($"\nUsing provider: {provider.Id}");

		string proceed = Helper.ReadLine("Proceed with the withdrawal? (y/n): ").Trim();
		if (!string.Equals(proceed, "y", StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException("Withdrawal cancelled");
		}

		AssetTransfer transfer;
		try
		{
			transfer = await assetMovementClient.InitiateTransferAsync(
				provider,
				new AssetTransferRequest(
					assetPair,
					new AssetTransferSource(keetaSource),
					new AssetTransferDestination(Constants.BankAccountUsLocation, bankRecipient),
					amountToWithdraw.ToString(CultureInfo.InvariantCulture)),
				cancellationToken);
		}
		catch (KeetaException error) when (error.Code == Blockers.KycShareNeededCode)
		{
			Console.Error.WriteLine(
				"KYC attributes must be shared with the provider before a USD withdrawal can be initiated.");
			Console.Error.WriteLine(
				"Complete KYC sharing (see anchor/kyc-client-sharekyc), then run this example again.");
			return 0;
		}
		catch (KeetaException error) when (error.Code == Blockers.UserActionNeededCode)
		{
			UserActionNeededBlocker? userActionNeeded = Blockers.TryParseUserActionNeeded(error);
			Console.Error.WriteLine(
				"Provider onboarding steps are still required before a USD withdrawal can be initiated.");
			Console.Error.WriteLine(
				"Complete the actions below (see anchor/kyc-client-sharekyc), then run this example again.");
			if (userActionNeeded is not null)
			{
				Console.Error.WriteLine(JsonSerializer.Serialize(userActionNeeded.ActionsNeeded, JsonOptions));
			}

			return 0;
		}
		catch (KeetaException error) when (error.Code == "SERVICE")
		{
			Console.Error.WriteLine("The provider rejected the withdrawal request.");
			Console.Error.WriteLine(
				"Check your bank details: routing number must be 9 digits, state must be a valid US code (e.g. NY, CA), and account type must be checking or savings.");
			Console.Error.WriteLine(error.Message);
			return 1;
		}

		Console.WriteLine($"\nTransfer initiated with ID: {transfer.Id}");
		Console.WriteLine(JsonSerializer.Serialize(transfer.InstructionChoices, JsonOptions));

		JsonElement instruction = transfer.InstructionChoices[0];
		if (instruction.GetProperty("type").GetString() != "KEETA_SEND")
		{
			throw new InvalidOperationException("Expected KEETA_SEND instruction not found");
		}

		string anchorAccount = instruction.GetProperty("sendToAddress").GetString()
			?? throw new InvalidOperationException("Expected sendToAddress in instruction");

		if (!instruction.TryGetProperty("external", out JsonElement externalElement))
		{
			throw new InvalidOperationException("Expected external field data in instruction");
		}

		string external = externalElement.ValueKind == JsonValueKind.String
			? externalElement.GetString()!
			: externalElement.GetRawText();

		Console.WriteLine("Sending USD to anchor ... please wait ...");

		using Account sendTo = runtime.Accounts.FromAccount(anchorAccount);
		await userClient.SendAsync(runtime, sendTo, amountToWithdraw, usdToken, external, cancellationToken);

		Console.WriteLine("\nMonitoring transfer status ... (checks every 5 seconds; Ctrl+C to stop)");

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

				AssetTransferStatus transactionResult = await transfer.GetTransferStatusAsync(monitorToken);
				string? status = transactionResult.Transaction.TryGetProperty("status", out JsonElement statusElement)
					? statusElement.GetString()
					: null;

				int elapsed = (int)(DateTimeOffset.UtcNow - startTime).TotalSeconds;
				Console.WriteLine($"\n[{elapsed}s] Status: {status ?? transactionResult.Transaction.GetRawText()}");

				if (string.Equals(status, "PROCESSING", StringComparison.Ordinal)
					|| string.Equals(status, "COMPLETED", StringComparison.Ordinal)
					|| string.Equals(status, "COMPLETE", StringComparison.Ordinal))
				{
					string accountEnding = accountNumber.Length >= 4
						? accountNumber[^4..]
						: accountNumber;

					Console.WriteLine($"""

						========================================
						  WITHDRAWAL PROCESSED SUCCESSFULLY!
						========================================
						Transfer ID: {transfer.Id}
						Amount: {Helper.FormatDecimals(amountToWithdraw, usdDecimals.Value)} USD
						From: Keeta Test Network
						To: US bank account ending {accountEnding}
						========================================
						""");

					BigInteger finalBalance = await userClient.BalanceAsync(usdToken, monitorToken);
					Console.WriteLine($"Final USD Balance on Keeta: {Helper.FormatDecimals(finalBalance, usdDecimals.Value)} USD");
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

	private static void ValidateUsBankDetails(string routingNumber, string accountType, string subdivision)
	{
		if (routingNumber.Length != 9 || !routingNumber.All(char.IsDigit))
		{
			throw new InvalidOperationException("US bank routing number must be exactly 9 digits.");
		}

		if (accountType is not ("checking" or "savings"))
		{
			throw new InvalidOperationException("Account type must be checking or savings.");
		}

		if (!UsStateCodes.Contains(subdivision))
		{
			throw new InvalidOperationException("State must be a valid 2-letter US code (e.g. NY, CA).");
		}
	}
}
