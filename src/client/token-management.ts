#! /usr/bin/env ts-node

/*
 * Description: Example of using the Keeta Network Client to Create and Manage a Token.
 */

import * as KeetaNet from '@keetanetwork/keetanet-client';
import { debugPrintableObject as DPO, getFaucetTokens } from '../helper.js';
import * as util from 'util';

const Account = KeetaNet.lib.Account;
const network = 'test';

async function main() {
	// Generate a random seed and user accounts using different KeyAlgorithm
	const seed = Account.generateRandomSeed({ asString: true });
	const userAccount = Account.fromSeed(seed, 0);
	// Create UserClient for Network and User Account
	await using userClient = KeetaNet.UserClient.fromNetwork(network, userAccount);

	// Get Tokens from the Faucet for Fees
	const faucetRequest = await getFaucetTokens(userAccount, network);

	if (!faucetRequest) {
		throw(new Error('Failed to get Faucet Tokens'));
	}

	// Create a Token on the Network
	const { account: token } = await userClient.generateIdentifier(Account.AccountKeyAlgorithm.TOKEN);
	if (!token.isToken()) {
		// basic type narrowing for AccountKeyAlgorithm.TOKEN
		throw(new Error('Tokens Should be TOKEN Key Algorithm'));
	}

	// Metadata like number of decimals places can be stored in the token account info
	const basicMetadata = Buffer.from(JSON.stringify({ decimalPlaces: 10 }), 'utf-8').toString('base64');

	// Initialize a Builder to batch multiple operations
	const builder = userClient.initBuilder();
	builder.setInfo({
		name: 'TKNA',
		description: `Example Token`,
		metadata: basicMetadata,
		// Default Permissions apply to every transaction using this token.  ACCESS grants everyone access to the token
		defaultPermission: new KeetaNet.lib.Permissions(['ACCESS'])
		// Because the builder was initialized with the user account we need to specify which account want to set the info on
	}, { account: token });

	// Setup Initial Token Supply - we use 50_000 * number of decimals/precision to get the raw amount
	builder.modifyTokenSupply(50_000n * (10n ** 10n), { account: token });

	/**
	 * Compute blocks to ensure token supply is added before sending.
	 * The builder does not always guarantee correct order so we compute the supply blocks to ensure they come first
	 */
	await builder.computeBlocks();

	// Send 200 TKNA Supply to user account
	builder.send(userAccount, 200n * (10n ** 10n), token, undefined, { account: token });

	// Compute Final Blocks
	await builder.computeBlocks();

	console.log(`User Seed: ${seed}`);
	console.log(`User Account: ${userAccount.publicKeyString.get()}`);
	console.log(`$TKNA Token: ${token.publicKeyString.get()}`);
	console.log(`Token Setup Blocks: ${[builder.blocks.map(block => block.hash.toString())]}`);

	// Publish builder blocks to the network
	await builder.publish();

	// Get final balances to compare after swap
	const userBalance = await userClient.allBalances();
	const tokenBalance = await userClient.client.getAllBalances(token);
	const tokenInfo = await userClient.client.getAccountInfo(token);

	console.log(`Initial User Balance: ${util.inspect(DPO(userBalance), { depth: 4, colors: true })}`);
	console.log(`Token Balance: ${util.inspect(DPO(tokenBalance), { depth: 4, colors: true })}`);
	console.log(`Token Info: ${util.inspect(DPO(tokenInfo), { depth: 4, colors: true })}`);
}

main().then(function() {
	process.exit(0);
}, function(err: unknown) {
	console.error(err);
	process.exit(1);
});
