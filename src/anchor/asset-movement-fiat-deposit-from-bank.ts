#! /usr/bin/env ts-node

/*
 * Description: Example of using the Keeta Anchor client to request USD bank deposit info (persistent address) for USD;
 * This example assumes that KYC has already been completed and onboarding has been performed.
 */

import * as KeetaAnchor from '@keetanetwork/anchor';
import { Errors } from '@keetanetwork/anchor/services/asset-movement/common.js';
import { debugPrintableObject as DPO, MetadataRoot, promptUser } from '../helper.js';
import * as util from 'util';

const Account = KeetaAnchor.KeetaNet.lib.Account;

const DEBUG = false;
const logger = DEBUG ? { logger: console } : {};
const network = 'test';

const KEETA_USD_ASSET = Account.fromPublicKeyString('keeta_any4zllibya6fum3lsoimxmnmeo57nklxlh4c6d6xosfacarfaa3knkiprkmm');
const US_BANK_SOURCE = { type: 'bank-account', account: { type: 'us' }} as const;

async function main() {
	console.log('Keeta Fiat Deposit Example: USD Bank Deposit Information');
	console.log('==========================================================\n');

	const seed = await promptUser('Enter your Keeta SEED with KYC completed: ');
	if (!seed.trim()) {
		throw(new Error('Invalid seed'));
	}
	const account = Account.fromSeed(seed.trim(), 0);

	console.log(`Keeta Account: ${account.publicKeyString.get()}`);
	console.log(`USD Token: ${KEETA_USD_ASSET.publicKeyString.get()}\n`);

	await using userClient = KeetaAnchor.KeetaNet.UserClient.fromNetwork(network, account);

	const assetMovementClient = new KeetaAnchor.AssetMovement.Client(userClient, {
		root: Account.fromPublicKeyString(MetadataRoot),
		...logger
	});

	const keetaDestination = {
		type: 'chain',
		chain: { type: 'keeta', networkId: userClient.network }
	} as const;

	const assetPair = { from: 'USD' as const, to: KEETA_USD_ASSET };

	const providers = await assetMovementClient.getProvidersForTransfer({
		asset: assetPair,
		from: US_BANK_SOURCE,
		to: keetaDestination
	});

	if (!providers || providers.length === 0) {
		throw(new Error('No asset movement providers found for USD bank deposit to Keeta'));
	}

	const provider = providers[0];
	if (!provider) {
		throw(new Error('Provider is undefined'));
	}

	console.log(`Using provider: ${String(provider.providerID)}\n`);

	try {
		const depositInfo = await provider.createPersistentForwardingAddress({
			account,
			asset: assetPair,
			sourceLocation: US_BANK_SOURCE,
			destinationLocation: keetaDestination,
			destinationAddress: account.publicKeyString.get()
		});

		console.log('USD bank deposit information:');
		console.log(util.inspect(DPO(depositInfo), { depth: 6, colors: true }));
	} catch (error) {
		if (Errors.KYCShareNeeded.isInstance(error)) {
			console.error('KYC attributes must be shared with the provider before a USD deposit address can be issued.');
			console.error('Complete KYC sharing (see kyc-client-sharekyc.ts), then run this example again.');
			return(true);
		}

		if (Errors.UserActionNeeded.isInstance(error)) {
			console.error('Provider onboarding steps are still required before a USD deposit address can be issued.');
			console.error('Complete the actions below (see kyc-client-sharekyc.ts), then run this example again.');
			console.error(util.inspect(DPO(error.actionsNeeded), { depth: 4, colors: true }));
			return(true);
		}
		throw(error);
	}
}

main().then(function() {
	process.exit(0);
}, function(err: unknown) {
	console.error(err);
	process.exit(1);
});
