#! /usr/bin/env ts-node

/*
 * Description: Example of using the Keeta Anchor Resolver to get metadata.
 */

import * as KeetaAnchor from '@keetanetwork/anchor';
import * as util from 'util';

const Account = KeetaAnchor.KeetaNet.lib.Account;

const network = 'test';
const fxServerURL = 'http://localhost:8080';

async function main() {
	const seed = Account.generateRandomSeed({ asString: true });
	const userAccount = Account.fromSeed(seed, 0);

	// Create UserClient for Network and User Account
	await using userClient = KeetaAnchor.KeetaNet.UserClient.fromNetwork(network, userAccount);

	const ktaToken = 'keeta_anyiff4v34alvumupagmdyosydeq24lc4def5mrpmmyhx3j6vj2uucckeqn52';
	const usdcToken = 'keeta_apna75yhhvnv4ei7ape55hndk4yepno7a7i2mhtiwahiygixjcnmvswxhnmnk';
	const testToken = 'keeta_anlzl53zghnkzjiovw7mb4i7zmovtygeajaffiaidz5sra4ky2vkhojr6urdw';

	// Set the Anchor Root Metadata
	await userClient.setInfo({
		description: 'FX Anchor Test Root',
		name: 'TEST',
		metadata: KeetaAnchor.lib.Resolver.Metadata.formatMetadata({
			version: 1,
			currencyMap: {
				'$KTA': ktaToken,
				'$USDC': usdcToken,
				'TEST': testToken // Base Sepolia TEST Token
			},
			services: {
				fx: {
					Test: {
						from: [
							{
								currencyCodes: [ktaToken],
								to: [usdcToken, testToken]
							},
							{
								currencyCodes: [usdcToken],
								to: [ktaToken, testToken]
							},
							{
								currencyCodes: [testToken],
								to: [ktaToken, usdcToken]
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

	const resolver = new KeetaAnchor.lib.Resolver({
		root: userAccount,
		client: userClient,
		trustedCAs: []
	});

	const metadata = await resolver.getRootMetadata();
	const resolvedMetadata = await KeetaAnchor.lib.Resolver.Metadata.fullyResolveValuizable(metadata);

	console.debug('Network Alias:', network);
	console.debug('User Account:', userAccount.publicKeyString.get());
	console.log(util.inspect(resolvedMetadata, { depth: 10, colors: true }));
}

main().then(function() {
	process.exit(0);
}, function(err: unknown) {
	console.error(err);
	process.exit(1);
});
