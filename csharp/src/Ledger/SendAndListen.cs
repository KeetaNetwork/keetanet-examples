using System.Numerics;
using KeetaNet.Anchor;
using KeetaNet.Anchor.Crypto;
using KeetaNet.Examples.Common;

namespace KeetaNet.Examples.Ledger;

public sealed class SendAndListenExample : IKeetaExample
{
	private const KeetaNetwork Network = KeetaNetwork.Test;

	public string Id => "client/send-and-listen";

	public string Description =>
		"Example of sending tokens and reacting to account changes over the representative's P2P socket";

	public async Task<int> Run(string[] args, CancellationToken cancellationToken = default)
	{
		using var runtime = WasmRuntime.Load();

		// Generate a random seed. The sender and the recipient derive from it
		// at different indexes.
		string seed = runtime.Accounts.GenerateRandomSeed();
		using Account senderAccount = runtime.Accounts.FromSeed(seed, 0, "ecdsa_secp256k1");
		using Account recipientAccount = runtime.Accounts.FromSeed(seed, 1, "ecdsa_secp256k1");
		using UserClient sender = runtime.CreateUserClient(Network, senderAccount);

		Console.WriteLine($"Seed: {seed}");
		Console.WriteLine($"Sender: {senderAccount.PublicKeyString}");
		Console.WriteLine($"Recipient: {recipientAccount.PublicKeyString}");

		// Get tokens from the faucet for the sends and their fees.
		if (!await Helper.GetFaucetTokens(sender, Network, cancellationToken))
		{
			throw new InvalidOperationException("Failed to get Faucet Tokens");
		}

		Account baseToken = sender.Client.BaseToken
			?? throw new InvalidOperationException("Client has no bound network base token");

		// Subscribe to the sender's account changes. The listener connects to
		// the representative's P2P WebSocket and re-reads the account when a
		// staple lands. A fallback poll covers anything the socket missed.
		using var changeSeen = new SemaphoreSlim(0);
		using IDisposable subscription = sender.OnChange(state =>
		{
			Console.WriteLine($"Change detected: head {state.HeadBlock}, height {state.HeadHeight}");
			changeSeen.Release();
		});

		// Each send advances the sender's chain, and the listener reports it.
		BigInteger amount = BigInteger.Pow(10, 9);
		for (int round = 1; round <= 2; round++)
		{
			Console.WriteLine($"Sending {amount} base tokens (round {round})...");
			if (!await sender.Send(recipientAccount, amount, baseToken, cancellationToken: cancellationToken))
			{
				throw new InvalidOperationException("Send failed");
			}

			if (!await changeSeen.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken))
			{
				throw new InvalidOperationException("No change notification arrived within 30 seconds");
			}
		}

		// Read the recipient and the history back from the network.
		BigInteger received = await sender.Client.GetBalance(recipientAccount, baseToken, cancellationToken);
		Console.WriteLine($"Recipient balance: {received}");

		HistoryPage history = await sender.GetHistory(cancellationToken: cancellationToken);
		Console.WriteLine($"Sender history entries: {history.Entries.Count}");

		return 0;
	}
}
