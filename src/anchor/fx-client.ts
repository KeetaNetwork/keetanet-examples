#! /usr/bin/env ts-node

/*
 * Description: Example of using the Keeta Client using the Test Network Demo FX Anchor
 */

import * as KeetaAnchor from '@keetanetwork/anchor';
import { debugPrintableObject as DPO, getTokenDecimals, getFaucetTokens } from '../helper.js';
import * as util from 'util';

const Account = KeetaAnchor.KeetaNet.lib.Account;

const DEBUG = false;
const logger = DEBUG ? { logger: console } : {};
const network = 'test';

async function main() {
	// Generate a random seed and `userAccount` that can perform swaps with the FX Anchor
	const seed = Account.generateRandomSeed({ asString: true });
	const userAccount = Account.fromSeed(seed, 0);

	console.log(`Seed: ${seed}`);
	console.log(`User Account: ${userAccount.publicKeyString.get()}`);

	// Create UserClient for Network and User Account
	await using userClient = KeetaAnchor.KeetaNet.UserClient.fromNetwork(network, userAccount);

	const baseTokenDecimals = await getTokenDecimals(network);
	if (baseTokenDecimals === null) {
		throw(new Error(`Failed to get Base Token Decimals for Network: ${network}`));
	}

	// Get Tokens from the Faucet for Fees
	const faucetRequest = await getFaucetTokens(userAccount, network);

	if (!faucetRequest) {
		throw(new Error('Failed to get Faucet Tokens'));
	}

	// FX Client to interact with FX HTTP Server
	const fxClient = new KeetaAnchor.FX.Client(userClient, {
		/**
		 * Network Address holds the default Anchor Metadata
		 * This can be changed to a custom root account if setting up custom metadata
		 * For example connecting to a custom FX Anchor
		 */
		root: userClient.networkAddress,
		...logger
	});

	// List all available tokens to swap using the Anchor Resolver
	const availableSwapTokens = await fxClient.resolver.listTokens();
	console.log(`Available Swap Tokens: ${util.inspect(DPO(availableSwapTokens), { depth: 10, colors: true })}`);

	// List all available conversion pairs available for KTA Base Token
	const availableConversions = await fxClient.listPossibleConversions({ from: userClient.baseToken });
	console.log(`Available Conversion Pairs: ${util.inspect(DPO(availableConversions), { depth: 10, colors: true })}`);

	// getQuotes returns any FX providers that can fulfil the requested swap
	const fxQuoteWithProviders = await fxClient.getQuotes({
		// Token we want to send
		from: userClient.baseToken.publicKeyString.get(),
		// Token we want to receive
		to: '$USDC',
		// Amount to exchange. Token is determined from `affinity`
		amount: 1 * 10 ** baseTokenDecimals, // 1 KTA
		// Direction of the exchange and what the amount should apply too
		affinity: 'from'
	});

	if (fxQuoteWithProviders === null) {
		throw(new Error('Failed to get FX Providers for Exchange'));
	}

	// In this example there is only 1 FX provider, so pick the first one
	const fxQuoteProvider = fxQuoteWithProviders[0];
	if (fxQuoteProvider === undefined) {
		throw(new Error('FX Provider is undefined'));
	}
	const quote = fxQuoteProvider.quote;

	// Get final balances to compare after swap
	const initialBalances = await userClient.allBalances();

	await fxQuoteProvider.createExchange();

	// Get final balances to compare after swap
	const finalBalances = await userClient.allBalances();

	console.log(`Exchange Quote: ${util.inspect(DPO(quote), { depth: 4, colors: true })}`);
	console.log(`Initial User Balances: ${util.inspect(DPO(initialBalances), { depth: 4, colors: true })}`);
	console.log(`Final User Balances: ${util.inspect(DPO(finalBalances), { depth: 4, colors: true })}`);
}

main().then(function() {
	process.exit(0);
}, function(err: unknown) {
	console.error(err);
	process.exit(1);
});
