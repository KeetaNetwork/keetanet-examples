using System.Numerics;
using System.Text;
using System.Text.Json;
using KeetaNet.Anchor;
using KeetaNet.Anchor.Crypto;
using KeetaNet.Examples.Common;

namespace KeetaNet.Examples.Ledger;

public sealed class TokenManagementExample : IKeetaExample
{
	private const KeetaNetwork Network = KeetaNetwork.Test;
	private const int DecimalPlaces = 10;

	public string Id => "client/token-management";

	public string Description =>
		"Example of using the Keeta Network Client to Create and Manage a Token";

	public async Task<int> Run(string[] args, CancellationToken cancellationToken = default)
	{
		using var runtime = WasmRuntime.Load();

		// Generate a random seed and user account.
		string seed = runtime.Accounts.GenerateRandomSeed();
		using Account userAccount = runtime.Accounts.FromSeed(seed, 0, "ecdsa_secp256k1");
		using UserClient userClient = runtime.CreateUserClient(Network, userAccount);

		// Get tokens from the faucet for fees.
		if (!await Helper.GetFaucetTokens(userClient, Network, cancellationToken))
		{
			throw new InvalidOperationException("Failed to get Faucet Tokens");
		}

		// Create a token on the network.
		using Account token = await userClient.GenerateIdentifier(
			IdentifierKind.Token, cancellationToken: cancellationToken);

		// Metadata like the number of decimal places is stored in the token
		// account info.
		string basicMetadata = Convert.ToBase64String(
			Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { decimalPlaces = DecimalPlaces })));

		BigInteger scale = BigInteger.Pow(10, DecimalPlaces);

		// The token setup block runs on the token's own chain, signed by the
		// owner. ACCESS as the default permission grants everyone access to
		// the token.
		using Permissions access = runtime.Blocks.PermissionsFromFlags(new[] { BaseFlag.Access });
		using BlockOperation setInfo = runtime.Blocks.SetInfo(
			"TKNA", "Example Token", basicMetadata, access);
		using BlockOperation supply = runtime.Blocks.TokenAdminSupply(50_000 * scale, AdjustMethod.Add);

		using Block setupBlock = BuildTokenBlock(runtime, userAccount, token, previous: null, setInfo, supply);

		// Send 200 TKNA of the fresh supply from the token to the user, chained
		// atop the setup block.
		using BlockOperation send = runtime.Blocks.Send(userAccount, 200 * scale, token);
		using Block sendBlock = BuildTokenBlock(runtime, userAccount, token, setupBlock.Hash, send);

		Console.WriteLine($"User Seed: {seed}");
		Console.WriteLine($"User Account: {userAccount.PublicKeyString}");
		Console.WriteLine($"$TKNA Token: {token.PublicKeyString}");
		Console.WriteLine($"Token Setup Blocks: {setupBlock.Hash}, {sendBlock.Hash}");

		// Publish both blocks to the network. The user pays any demanded fee.
		bool published = await userClient.Client.Transmit(
			new[] { setupBlock, sendBlock },
			TransmitOptions.WithFeeSigner(userAccount),
			cancellationToken);
		if (!published)
		{
			throw new InvalidOperationException("Failed to publish the token setup blocks");
		}

		// Read the final state back from the network.
		IReadOnlyList<TokenBalance> userBalances = await userClient.GetAllBalances(cancellationToken);
		IReadOnlyList<TokenBalance> tokenBalances = await userClient.Client.GetAllBalances(token, cancellationToken);
		AccountState tokenInfo = await userClient.Client.GetAccountInfo(token, cancellationToken);

		Console.WriteLine($"User Balances: {FormatBalances(userBalances)}");
		Console.WriteLine($"Token Balances: {FormatBalances(tokenBalances)}");
		Console.WriteLine($"Token Info: {tokenInfo.Info?.Name} ({tokenInfo.Info?.Description}), supply {tokenInfo.Info?.Supply}");

		return 0;
	}

	/// <summary>Build one signed block on the token's chain with the given operations.</summary>
	private static Block BuildTokenBlock(
		WasmRuntime runtime,
		Account owner,
		Account token,
		BlockHash? previous,
		params BlockOperation[] operations)
	{
		using BlockBuilder builder = runtime.Blocks.NewBuilder();
		builder
			.WithVersion(2)
			.WithNetwork(Network.Id())
			.WithAccount(token)
			.WithSigner(owner)
			.WithDate(DateTimeOffset.UtcNow);

		if (previous is { } hash)
		{
			builder.WithPrevious(hash);
		}
		else
		{
			builder.AsOpening();
		}

		foreach (BlockOperation operation in operations)
		{
			builder.AddOperation(operation);
		}

		return builder.Build();
	}

	private static string FormatBalances(IReadOnlyList<TokenBalance> balances) =>
		string.Join(", ", balances.Select(entry => $"{entry.Token.PublicKeyString}={entry.Balance}"));
}
