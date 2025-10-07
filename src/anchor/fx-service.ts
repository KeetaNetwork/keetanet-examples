#! /usr/bin/env ts-node

/*
 * Description: Example of using the Keeta FX Server and Client.
 */

import * as KeetaAnchor from '@keetanetwork/anchor';
import { debugPrintableObject, getBaseTokenDecimals, getFaucetTokens } from '../helper.js';
import { KeetaNetFXAnchorHTTPServer } from '@keetanetwork/anchor/services/fx/server.js';
import type { TokenAddress } from '@keetanetwork/keetanet-client/lib/account.js';
import * as util from 'util';

const Account = KeetaAnchor.KeetaNet.lib.Account;

const DEBUG = false;
const logger = DEBUG ? { logger: console } : {};
const network = 'test';

async function main() {
	/**
	 * Generate a random seed to base the accounts on, these accounts are
	 * `userAccount` is a user account that creates two tokens and can perform swaps with the FX Anchor
	 * `liqudityProvider` is the account which holds the FX Anchor liquidity
	 */
	const seed = Account.generateRandomSeed({ asString: true });
	const userAccount = Account.fromSeed(seed, 0);
	const liquidityProvider = Account.fromSeed(seed, 1);

	console.log(`Seed: ${seed}`);
	console.log(`User Account: ${userAccount.publicKeyString.get()}`);
	console.log(`Liquidity FX Provider: ${liquidityProvider.publicKeyString.get()}`);

	// Create UserClient for Network and User Account
	await using userClient = KeetaAnchor.KeetaNet.UserClient.fromNetwork(network, userAccount);

	const baseTokenDecimals = await getBaseTokenDecimals(network);
	if (baseTokenDecimals === null) {
		throw(new Error(`Failed to get Base Token Decimals for Network: ${network}`));
	}

	// Get Tokens from the Faucet for Fees
	const faucetRequest = await getFaucetTokens(userAccount, network);

	if (!faucetRequest) {
		throw(new Error('Failed to get Faucet Tokens'));
	}

	/**
	 * Create 2 tokens for the FX Anchor to operate on, 1 to swap for the other
	 * TKNA: The token the user account initially has
	 * TKNB: The token the user account wants to receive
	 */
	const { account: token1 } = await userClient.generateIdentifier(Account.AccountKeyAlgorithm.TOKEN);
	const { account: token2 } = await userClient.generateIdentifier(Account.AccountKeyAlgorithm.TOKEN);
	if (!token1.isToken() || !token2.isToken()) {
		// basic type narrowing for AccountKeyAlgorithm.TOKEN
		throw(new Error('Tokens Should be TOKEN Key Algorithm'));
	}

	const initialTokenSupply = 50_000n;

	// Create a builder to construct the blocks
	const builder = userClient.initBuilder();

	// Set token info and metadata with base permission of ACCESS for anyone to use the token
	const basicMetadata = Buffer.from(JSON.stringify({ decimalPlaces: 6 }), 'utf-8').toString('base64');
	for (const tokenInfo of [{ token: token1, name: 'TKNA' }, { token: token2, name: 'TKNB' }]) {
		builder.setInfo({
			name: tokenInfo.name,
			description: `Example Token ${tokenInfo.name}`,
			metadata: basicMetadata,
			defaultPermission: new KeetaAnchor.KeetaNet.lib.Permissions(['ACCESS'])
		}, { account: tokenInfo.token });

		// Setup Initial Token Supplies and Distribute Amounts to Liquidity Pool
		builder.modifyTokenSupply(initialTokenSupply, { account: tokenInfo.token });

		/**
		 * Compute blocks to ensure token supply is added before sending.
		 * The builder does not always guarantee correct order so we compute the supply blocks to ensure they come first
		 */
		await builder.computeBlocks();
		builder.send(liquidityProvider, 10_000n, tokenInfo.token, undefined, { account: tokenInfo.token });
	}

	// Send some TKNA Supply to user account
	builder.send(userAccount, 10_000n, token1, undefined, { account: token1 });

	// Send some KTA Base Token to the liquidity provider so it can pay fees
	builder.send(liquidityProvider, 2n * (10n ** BigInt(baseTokenDecimals)), userClient.baseToken);

	// Compute Final Blocks
	await builder.computeBlocks();

	console.log(`$TKNA Token: ${token1.publicKeyString.get()}`);
	console.log(`$TKNB Token: ${token2.publicKeyString.get()}`);
	console.log(`Token Setup Blocks: ${[builder.blocks.map(block => block.hash.toString())]}`);

	// Publish builder blocks to the network
	await builder.publish();

	await using liquidityProviderUserClient = KeetaAnchor.KeetaNet.UserClient.fromNetwork(network, liquidityProvider);
	// Setup FX Anchor HTTP Server
	await using fxServer = new KeetaNetFXAnchorHTTPServer({
		...logger,
		account: liquidityProvider,
		quoteSigner: liquidityProvider,
		client: liquidityProviderUserClient,
		fx: {
			// Provided function to calculate the rate
			getConversionRateAndFee: async function(request) {
				let rate = 0.88;
				// Affinity could be 'from' or 'to' and can change which direction the rate should be calculated
				if (request.affinity === 'to') {
					rate = 1 / rate;
				}
				/**
				 * Convert the request amount to bigint
				 * Multiple the rate by the number of decimals (6 in this example) for the token so we can do bigint math
				 */
				const fixedDecimalRate = (rate * (10 ** 6)).toFixed(0);
				const convertedAmount = (BigInt(request.amount) * BigInt(fixedDecimalRate)) / BigInt((10 ** 6));
				return({
					account: liquidityProvider.publicKeyString.get(),
					convertedAmount: convertedAmount.toString(),
					cost: {
						amount: '0',
						token: token1.publicKeyString.get()
					}
				});
			}
		}
	});

	// Start the HTTP Server - getEstimate, getQuote, createExchange and getExchangeStatus endpoints are already defined in the KeetaNetFxAnchorHTTPServer class
	await fxServer.start();
	const fxServerURL = fxServer.url;

	// Set the Anchor Root Metadata
	await userClient.setInfo({
		description: 'FX Anchor Test Root',
		name: 'TEST',
		metadata: KeetaAnchor.lib.Resolver.Metadata.formatMetadata({
			version: 1,
			currencyMap: {
				'$TKNA': token1.publicKeyString.get(),
				'$TKNB': token2.publicKeyString.get()
			},
			services: {
				fx: {
					Test: {
						from: [
							{
								currencyCodes: [token1.publicKeyString.get()],
								to: [token2.publicKeyString.get()]
							},
							{
								currencyCodes: [token2.publicKeyString.get()],
								to: [token1.publicKeyString.get()]
							}
						],
						operations: {
							getEstimate: `${fxServerURL}/api/getEstimate`,
							getQuote: `${fxServerURL}/api/getQuote`,
							createExchange: `${fxServerURL}/api/createExchange`,
							getExchangeStatus: `${fxServerURL}/api/getExchangeStatus/{exchangeID}`
						}
					}
				}
			}
		})
	});

	// FX Client to interact with FX HTTP Server
	const fxClient = new KeetaAnchor.FX.Client(userClient, {
		root: userAccount,
		signer: userAccount,
		account: userAccount,
		...logger
	});

	// Get initial balances for User Account before the swap occurs to compare
	const initialBalances = await userClient.allBalances();

	// Details of the swap request we want to make
	const swapRequest: { from: TokenAddress, to: TokenAddress, amount: string, affinity: 'from' | 'to' } = {
		// Token we want to send
		from: token1,
		// Token we want to receive
		to: token2,
		// Amount to exchange.  Token is determined from `affinity`
		amount: '100',
		// Direction of the exchange and what the amount should apply too
		affinity: 'from'
	}

	// getQuotes returns any FX providers that can fulfil the requested swap
	const fxQuoteWithProviders = await fxClient.getQuotes(swapRequest);
	if (fxQuoteWithProviders === null) {
		throw(new Error('Failed to get FX Providers for Exchange'));
	}

	// In this example there is only 1 FX provider, so pick the first one
	const fxQuoteProvider = fxQuoteWithProviders[0];
	if (fxQuoteProvider === undefined) {
		throw(new Error('FX Provider is undefined'));
	}
	const quote = fxQuoteProvider.quote;

	// Create the swap request block.  Defined amount being sent from our account
	const from = { account: userAccount, token: swapRequest.from, amount: BigInt(swapRequest.amount) };

	// Define amount to be received/swapped with the FX provider using the converted amount provided in the quote
	const to = { account: Account.fromPublicKeyString(quote.account), token: swapRequest.to, amount: BigInt(quote.convertedAmount) };
	/**
	 * Create the swap block.
	 * This is optional and will be handled by the FX Client automatically
	 * If the block is not provided, the FX Client will use initial quote request parameters to create one
	 */
	const createSwapBlock = await userClient.createSwapRequest({ from, to });
	await fxQuoteProvider.createExchange(createSwapBlock);

	// Get final balances to compare after swap
	const finalBalances = await userClient.allBalances();
	const liquidityBalances = await userClient.client.getAllBalances(liquidityProvider);

	console.log(`Exchange Quote: ${util.inspect(debugPrintableObject(fxQuoteProvider.quote), { depth: 4, colors: true })}`);
	console.log(`Initial User Balances: ${util.inspect(debugPrintableObject(initialBalances), { depth: 4, colors: true })}`);
	console.log(`Final User Balances: ${util.inspect(debugPrintableObject(finalBalances), { depth: 4, colors: true })}`);
	console.log(`Liquidity Provider Final Balances: ${util.inspect(debugPrintableObject(liquidityBalances), { depth: 4, colors: true })}`);
}

main().then(function() {
	process.exit(0);
}, function(err: unknown) {
	console.error(err);
	process.exit(1);
});
