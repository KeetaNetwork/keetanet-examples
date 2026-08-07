using System.Text;
using System.Text.Json;
using KeetaNet.Anchor;
using KeetaNet.Anchor.Crypto;
using CryptoContainer = KeetaNet.Anchor.Crypto.EncryptedContainer;

namespace KeetaNet.Examples.Anchor;

public sealed class EncryptedContainerExample : IKeetaExample
{
	public string Id => "anchor/encrypted-container";

	public string Description =>
		"Example of using the Keeta Anchor Client to create an encrypted container";

	public Task<int> Run(string[] args, CancellationToken cancellationToken = default)
	{
		using var runtime = WasmRuntime.Load();

		// Create accounts.
		string seed1 = runtime.Accounts.GenerateRandomSeed();
		using Account sender = runtime.Accounts.FromSeed(seed1, 0, "ecdsa_secp256k1");

		string seed2 = runtime.Accounts.GenerateRandomSeed();
		using Account recipient = runtime.Accounts.FromSeed(seed2, 0, "ecdsa_secp256k1");

		// Create sensitive data.
		const string privateData = "sensitive information that only the recipient should see";
		byte[] plaintext = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(privateData));

		// Create an encrypted container that only the recipient can decrypt.
		// The signer proves authenticity. Locked containers refuse new
		// recipients later.
		using CryptoContainer container = runtime.Containers.FromPlaintext(
			plaintext,
			principals: new[] { recipient },
			locked: false,
			signer: sender);

		// Serialize the container for transmission.
		byte[] encodedContainer = container.GetEncoded();
		Console.WriteLine($"Encrypted container size: {encodedContainer.Length} bytes");

		// The recipient can decrypt the container with its private key.
		using CryptoContainer receivedContainer = runtime.Containers.FromEncoded(
			encodedContainer,
			new[] { recipient });

		// Extract the plaintext.
		byte[] decryptedData = receivedContainer.GetPlaintext();
		string parsed = Encoding.UTF8.GetString(decryptedData);
		Console.WriteLine($"Decrypted data: {parsed}");

		// Verify the signature if it was signed.
		if (receivedContainer.IsSigned)
		{
			byte[]? signerKey = receivedContainer.GetSigningAccount();
			if (signerKey is not null)
			{
				using Account signerAccount = runtime.Accounts.FromPublicKeyAndType(
					Convert.ToHexString(signerKey));
				Console.WriteLine($"Signed by: {signerAccount.PublicKeyString}");
			}

			Console.WriteLine($"Signature valid: {receivedContainer.VerifySignature()}");
		}

		return Task.FromResult(0);
	}
}
