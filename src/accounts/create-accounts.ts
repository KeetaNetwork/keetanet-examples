#! /usr/bin/env ts-node

/*
 * Description: Example of using the Keeta Net Client to Create Different Accounts Types.
 */

import * as KeetaNet from '@keetanetwork/keetanet-client';
import { debugPrintableObject as DPO } from '../helper.js';

const Account = KeetaNet.lib.Account;
const network = 'test';
const networkConfig = KeetaNet.Client.Config.getDefaultConfig(network);

async function main() {
	// Generate a random seed and user accounts using different KeyAlgorithm
	const seed = Account.generateRandomSeed({ asString: true });
	const secp256K1Account = Account.fromSeed(seed, 0);  // default KeyAlgorithm is ECDSA_SECP256K1
	const secp256R1Account = Account.fromSeed(seed, 0, Account.AccountKeyAlgorithm.ECDSA_SECP256R1);
	const ed25519Account = Account.fromSeed(seed, 0, Account.AccountKeyAlgorithm.ED25519);

	/**
	 * Identifiers are constructed with a User Account, which becomes the owner of the identifier.
	 * Identifiers cannot sign blocks on their own chain and must have an owner or another
	 * account that has been delegated permissions to add blocks to the network
	 * TOKEN - Tokens that can have a supply and be transferred between users on the network
	 * STORAGE - Storage accounts are useful for segragating funds or sharing with other users
	 * MULTISIG - Multisig identifier which can be used with multiple signers and a `quorum` set to sign blocks
	 *
	 * BlockHash provided as second parameter should match the account head or `previous` that will be used
	 * OperationIndex corresponds to the index of the operation in the block that creates the identifier
	 */
	const token = secp256K1Account.generateIdentifier(Account.AccountKeyAlgorithm.TOKEN, undefined, 0);
	const storage = secp256K1Account.generateIdentifier(Account.AccountKeyAlgorithm.STORAGE, undefined, 1);
	const multisig = secp256K1Account.generateIdentifier(Account.AccountKeyAlgorithm.MULTISIG, undefined, 2);

	/**
	 * Example Block that could be constructed to produce these identifiers
	 * See Client Examples for simpler methods to construct and use different identifiers on the network
	 */
	const block = await new KeetaNet.lib.Block.Builder({
		account: secp256K1Account,
		network: networkConfig.network,
		previous: KeetaNet.lib.Block.NO_PREVIOUS,
		operations: [
			{
				type: KeetaNet.lib.Block.OperationType.CREATE_IDENTIFIER,
				identifier: token
			},
			{
				type: KeetaNet.lib.Block.OperationType.CREATE_IDENTIFIER,
				identifier: storage
			},
			{
				type: KeetaNet.lib.Block.OperationType.CREATE_IDENTIFIER,
				identifier: multisig,
				createArguments: {
					type: KeetaNet.lib.Account.AccountKeyAlgorithm.MULTISIG,
					signers: [secp256K1Account, secp256R1Account, ed25519Account],
					quorum: 2n
				}
			}
		]
	}).seal();

	/**
	 * To actually create these identifiers the block would need to be published to the network
	 * for this example we stop here.  See the client examples for publishing to the network.
	 */

	console.debug('Network Alias:', network);
	console.debug('SECP256K1 Account:', secp256K1Account.publicKeyString.get());
	console.debug('SECP256R1 Account:', secp256R1Account.publicKeyString.get());
	console.debug('ED25519 Account:', ed25519Account.publicKeyString.get());
	console.debug('Token Identifier:', token.publicKeyString.get());
	console.debug('Storage Identifier:', storage.publicKeyString.get());
	console.debug('Multisig Identifier:', multisig.publicKeyString.get());
	console.debug('Block:', DPO(block.toJSON()));
}

main().then(function() {
	process.exit(0);
}, function(err: unknown) {
	console.error(err);
	process.exit(1);
});
