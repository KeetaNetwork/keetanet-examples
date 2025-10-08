#! /usr/bin/env ts-node

/*
 * Description: Example of using the Keeta FX HTTP Server.
 * This example starts a very basic FX Server that provides the necessary endpoints
 * The KeetaNetFXAnchorHTTPServer class includes logic for handling the swap blocks and publishing
 * The user is responsible for providing the configuration including a method for getConversionRateAndFee
 */

import * as KeetaAnchor from '@keetanetwork/anchor';
import { KeetaNetFXAnchorHTTPServer } from '@keetanetwork/anchor/services/fx/server.js';
import { getBaseTokenDecimals } from '../helper.js';

const Account = KeetaAnchor.KeetaNet.lib.Account;

const DEBUG = false;
const logger = DEBUG ? { logger: console } : {};
const network = 'test';

async function main() {
	/**
	 * Generate a random seed to create a `liqudityProvider` ccount which holds the FX Anchor liquidity
	 * The liquidy provider should be granted access to the token pairs it will be managing
	 * Funds should be added separately
	 */
	const seed = Account.generateRandomSeed({ asString: true });
	const liquidityProvider = Account.fromSeed(seed, 0);

	console.log(`Seed: ${seed}`);
	console.log(`Liquidity FX Provider: ${liquidityProvider.publicKeyString.get()}`);

	const decimalPlaces = await getBaseTokenDecimals(network);
	if (decimalPlaces === null) {
		throw(new Error('Unable to get base token decimals'));
	}

	await using liquidityProviderUserClient = KeetaAnchor.KeetaNet.UserClient.fromNetwork(network, liquidityProvider);
	// Setup FX Anchor HTTP Server
	const fxServer = new KeetaNetFXAnchorHTTPServer({
		...logger,
		account: liquidityProvider,
		quoteSigner: liquidityProvider,
		client: liquidityProviderUserClient,
		fx: {
			/**
			 * Example function to calculate the rate
			 * This is purely an example and should not be used in a production scenario
			 * Decimals for both tokens involved in the swap should be consider
			 * As well as any other factors that should affect the conversion rate
			 * like external pricing, constant product formulae etc
			 */
			getConversionRateAndFee: async function(request) {
				let rate = 0.88;
				// Affinity could be 'from' or 'to' and can change which direction the rate should be calculated
				if (request.affinity === 'to') {
					rate = 1 / rate;
				}
				/**
				 * Convert the request amount to bigint
				 * Multiple the rate by the number of decimals for the token so we can do bigint math
				 * This should look at the actual decimals for the tokens in the request
				 */
				const fixedDecimalRate = Math.round((rate * (10 ** decimalPlaces)));
				const convertedAmount = (BigInt(request.amount) * BigInt(fixedDecimalRate)) / BigInt((10 ** decimalPlaces));
				return({
					account: liquidityProvider.publicKeyString.get(),
					convertedAmount: convertedAmount.toString(),
					cost: {
						amount: '0',
						token: liquidityProviderUserClient.baseToken.publicKeyString.get()
					}
				});
			}
		}
	});

	// Start the HTTP Server - getEstimate, getQuote, createExchange and getExchangeStatus endpoints are already defined in the KeetaNetFxAnchorHTTPServer class
	await fxServer.start();
	const fxServerURL = fxServer.url;
	console.log(`FX Server Start at ${fxServerURL}`);
	console.log('Use Ctrl+C to Stop the Server');

	// Run the shutdown logic
	await new Promise<void>((resolve) => {
		process.on('SIGINT', async () => {
			console.log('\nReceived Ctrl+C (SIGINT)');
			console.log('Shutting down server...');
			await fxServer.stop();
			resolve();
		});
	});
}

main().then(function() {
	process.exit(0);
}, function(err: unknown) {
	console.error(err);
	process.exit(1);
});
