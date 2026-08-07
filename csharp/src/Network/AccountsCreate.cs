using KeetaNet.Anchor;
using KeetaNet.Anchor.Crypto;

namespace KeetaNet.Examples.Network;

public sealed class AccountsCreateExample : IKeetaExample
{
	private const KeetaNetwork Network = KeetaNetwork.Test;

	public string Id => "client/accounts-create";

	public string Description =>
		"Example of using the Keeta Network Client to Create Different Accounts Types";

	public Task<int> Run(string[] args, CancellationToken cancellationToken = default)
	{
		using var runtime = WasmRuntime.Load();

		// Generate a random seed and user accounts using different key algorithms.
		string seed = runtime.Accounts.GenerateRandomSeed();
		using Account secp256K1Account = runtime.Accounts.FromSeed(seed, 0, "ecdsa_secp256k1");
		using Account secp256R1Account = runtime.Accounts.FromSeed(seed, 0, "ecdsa_secp256r1");
		using Account ed25519Account = runtime.Accounts.FromSeed(seed, 0, "ed25519");

		// Identifiers are derived from a user account, which becomes their
		// owner. Identifiers cannot sign blocks on their own chain: the owner
		// (or a delegate) publishes for them.
		//   TOKEN   - tokens with a supply, transferable between accounts
		//   STORAGE - segregates funds or shares them with other accounts
		// The index corresponds to the position of the creating operation in
		// the block below.
		using Account token = secp256K1Account.GenerateIdentifier(IdentifierKind.Token, index: 0);
		using Account storage = secp256K1Account.GenerateIdentifier(IdentifierKind.Storage, index: 1);

		// An example block that would produce these identifiers when
		// published. This example stops before publishing; see the ledger
		// examples for transmitting blocks to the network.
		using BlockOperation createToken = runtime.Blocks.CreateIdentifier(token);
		using BlockOperation createStorage = runtime.Blocks.CreateIdentifier(storage);

		using BlockBuilder builder = runtime.Blocks.NewBuilder();
		builder
			.WithVersion(2)
			.WithNetwork(Network.Id())
			.WithAccount(secp256K1Account)
			.WithSigner(secp256K1Account)
			.WithDate(DateTimeOffset.UtcNow)
			.AsOpening()
			.AddOperation(createToken)
			.AddOperation(createStorage);
		using Block block = builder.Build();

		Console.WriteLine($"Network Alias: {Network.Alias()}");
		Console.WriteLine($"SECP256K1 Account: {secp256K1Account.PublicKeyString}");
		Console.WriteLine($"SECP256R1 Account: {secp256R1Account.PublicKeyString}");
		Console.WriteLine($"ED25519 Account: {ed25519Account.PublicKeyString}");
		Console.WriteLine($"Token Identifier: {token.PublicKeyString}");
		Console.WriteLine($"Storage Identifier: {storage.PublicKeyString}");
		Console.WriteLine($"Block: {block.Hash}");

		return Task.FromResult(0);
	}
}
