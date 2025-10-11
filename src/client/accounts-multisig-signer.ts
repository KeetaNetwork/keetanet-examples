#! /usr/bin/env ts-node

/*
 * Description: Example of using the Keeta Network Client to Create a Multisig Identifier.
 */

import * as KeetaNet from '@keetanetwork/keetanet-client';
import { getFaucetTokens } from '../helper.js';

const Account = KeetaNet.lib.Account;
const network = 'test';

async function main() {
	// Generate a random seed and user account
	const seed = Account.generateRandomSeed({ asString: true });
	const userAccount = KeetaNet.lib.Account.fromSeed(seed, 0);
	const tokenRequest = await getFaucetTokens(userAccount, network);
	if (!tokenRequest) {
		throw(new Error('Failed to get tokens from faucet'));
	}

	// Create User Client from network and account
	const userClient = KeetaNet.UserClient.fromNetwork(network, userAccount);
	// Get the account head block hash - used for constructing blocks
	const userAccountHeadBlockHash = await userClient.head();

	// Generate 3 signer accounts
	const signer1 = KeetaNet.lib.Account.fromSeed(seed, 1);
	const signer2 = KeetaNet.lib.Account.fromSeed(seed, 2);
	const signer3 = KeetaNet.lib.Account.fromSeed(seed, 3);

	// Create Multisig Identifiers
	const multisigIdentifier = userAccount.generateIdentifier(KeetaNet.lib.Account.AccountKeyAlgorithm.MULTISIG, undefined, 0);

	// Construct block that creates the identifiers on the network when published
	const identifierBlock = await new KeetaNet.lib.Block.Builder({
		previous: userAccountHeadBlockHash ?? KeetaNet.lib.Block.NO_PREVIOUS,
		account: userAccount,
		network: userClient.network,
		operations: [
			{
				type: KeetaNet.lib.Block.OperationType.CREATE_IDENTIFIER,
				identifier: multisigIdentifier,
				createArguments: {
					type: KeetaNet.lib.Account.AccountKeyAlgorithm.MULTISIG,
					signers: [signer1, signer2, signer3],
					quorum: 2n
				}
			},
			// Grant Admin Permissions on the `userAccount` to the multisig
			{
				type: KeetaNet.lib.Block.OperationType.MODIFY_PERMISSIONS,
				method: KeetaNet.lib.Block.AdjustMethod.SET,
				principal: multisigIdentifier,
				permissions: [ ['ADMIN'], [] ]
			}
		]
	}).seal();

	// Publish the identifier block
	await userClient.transmit([ identifierBlock ]);

	// Similar to generateIdentifier on the `userAccount` but this method uses the UserClient and handles building and publishing the blocks automatically
	const { account: customToken } = await userClient.generateIdentifier(KeetaNet.lib.Account.AccountKeyAlgorithm.TOKEN);

	// Similar to MODIFY_PERMISSIONS in the manual block, this grants ADMIN Permissions to the multisign identifier on the `customToken`
	await userClient.updatePermissions(multisigIdentifier, new KeetaNet.lib.Permissions(['ADMIN']), undefined, KeetaNet.lib.Block.AdjustMethod.SET, { account: customToken });

	const tokenHeadBlockHash = await userClient.client.getHeadBlock(customToken);
	const basicMetadata = Buffer.from(JSON.stringify({ decimalPlaces: 6 }), 'utf-8').toString('base64');
	/**
	 * Create a Block using the multisig identifier and signers
	 * This block sets the token info (name, description, metadata and default permissions)
	 * Multisig Identifier was granted ADMIN access to the token so it can sign blocks for the `customToken`.
	 */
	const multisigExampleBlock = await new KeetaNet.lib.Block.Builder({
		version: 2,
		account: customToken,
		previous: tokenHeadBlockHash?.hash ?? KeetaNet.lib.Block.NO_PREVIOUS,
		// We created the multisig with quorum: 2 so we only need 2 out of 3 signers to be valid
		signer: [multisigIdentifier, [signer1, signer2]],
		network: userClient.network,
		operations: [{
			type: KeetaNet.lib.Block.OperationType.SET_INFO,
			name: 'TKNM',
			description:
			'Test Multisig Token Example',
			metadata: basicMetadata,
			// Grant public ACCESS to this token
			defaultPermission: new KeetaNet.lib.Permissions(['ACCESS'])
		}]
	}).seal();

	await userClient.transmit([multisigExampleBlock]);

	console.log(`Seed: ${seed}`);
	console.log(`User Account: ${userAccount.publicKeyString.get()}`);
	console.log(`MultiSig Account: ${multisigIdentifier.publicKeyString.get()}`);
	console.log(`Create MultiSig Block: ${identifierBlock.hash.toString()}`);
	console.log(`Custom Token: ${customToken.publicKeyString.get()}`);
	console.log(`Token MultiSig Block: ${multisigExampleBlock.hash.toString()}`);

	// Cleanup the user client
	await userClient.destroy();
}

main().then(function() {
	process.exit(0);
}, function(err: unknown) {
	console.error(err);
	process.exit(1);
});
