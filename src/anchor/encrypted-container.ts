#! /usr/bin/env ts-node

/*
 * Description: Example of using the Keeta Anchor Client to create an encrypted container
 * Encrypted containers can only be decrypted by the specified recipient(s)
 */
import * as KeetaAnchor from '@keetanetwork/anchor';
import * as KeetaNet from '@keetanetwork/keetanet-client';

async function main() {
	// Create accounts
	const seed1 = KeetaNet.lib.Account.generateRandomSeed({ asString: true });
	const sender = KeetaNet.lib.Account.fromSeed(seed1, 0);

	const seed2 = KeetaNet.lib.Account.generateRandomSeed({ asString: true });
	const recipient = KeetaNet.lib.Account.fromSeed(seed2, 0);

	// Create sensitive data
	const privateData = {
		documentType: 'passport',
		documentNumber: 'X1234567',
		fullName: 'Alice Smith',
		dateOfBirth: '1990-01-01'
	};

	const plaintext = Buffer.from(JSON.stringify(privateData), 'utf-8');

	// Create an encrypted container that only the recipient can decrypt
	const container = KeetaAnchor.lib.EncryptedContainer.fromPlaintext(
		plaintext,
		[recipient],  // List of accounts that can decrypt
		{
			signer: sender,  // Optionally sign to prove authenticity
			locked: false    // Set to true to prevent adding more recipients later
		}
	);

	// Serialize the container for transmission
	const encodedContainer = await container.getEncodedBuffer();
	console.log('Encrypted container size:', encodedContainer.byteLength, 'bytes');

	// The recipient can decrypt the container
	const receivedContainer = KeetaAnchor.lib.EncryptedContainer.fromEncodedBuffer(
		encodedContainer,
		[recipient]  // Provide the recipient's account (with private key)
	);

	// Extract the plaintext
	const decryptedData = await receivedContainer.getPlaintext();
	const parsed = Buffer.from(decryptedData).toString('utf-8');

	console.log('Decrypted data:', parsed);

	// Verify the signature if it was signed
	if (receivedContainer.isSigned) {
		const signerPublicKey = receivedContainer.getSigningAccount()?.publicKeyString.get();
		console.log('Signed by:', signerPublicKey);
		console.log('Signature valid:', await receivedContainer.verifySignature());
	}
}

main().then(function() {
	process.exit(0);
}, function(err: unknown) {
	console.error(err);
	process.exit(1);
});
