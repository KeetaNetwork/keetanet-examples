#! /usr/bin/env ts-node

/*
 * Description: Example of using the Keeta FX Server and Client.
 */

import * as KeetaAnchor from '@keetanetwork/anchor';
import { debugPrintableObject, getBaseTokenDecimals, getFaucetTokens, waitForResult } from '../helper.js';
import { KeetaNetFXAnchorHTTPServer } from '@keetanetwork/anchor/services/fx/server.js';
import * as util from 'util';

const Account = KeetaAnchor.KeetaNet.lib.Account;

const DEBUG = false;
const logger = DEBUG ? console : undefined;
const network = 'test';

async function main() {
    // Setup Accounts to test
    const seed = Account.generateRandomSeed({ asString: true});
    console.debug(`Seed: ${seed}`);
    const account = Account.fromSeed(seed, 0);
    console.debug(`Account: ${account.publicKeyString.get()}`);
    const liquidityProvider = Account.fromSeed(seed, 1);
    console.debug(`Liquidity FX Provider: ${liquidityProvider.publicKeyString.get()}`);

    // Create UserClient for TEST network
    const userClient = KeetaAnchor.KeetaNet.UserClient.fromNetwork(network, account);

    // Get 10 Tokens from the Faucet for fees
    await getFaucetTokens(account);

    // Wait for faucet distribution
    const balanceResult = await waitForResult(async function() {
        const balance = await userClient.balance(userClient.baseToken);
        return(balance > 0n);
    });
    if (!balanceResult) {
        throw(new Error('Did not get faucet balance within 15 seconds'));
    }

    // Create 2 tokens for FX
    const { account: token1 } = await userClient.generateIdentifier(Account.AccountKeyAlgorithm.TOKEN);
    const { account: token2 } = await userClient.generateIdentifier(Account.AccountKeyAlgorithm.TOKEN);
    if (!token1.isToken() || !token2.isToken()) {
        // basic type narrowing for TOKEN
        throw(new Error('Tokens Should be TOKEN Key Algorithm'));
    }
    console.debug(`$TKNA Token: ${token1.publicKeyString.get()}`);
    console.debug(`$TKNB Token: ${token2.publicKeyString.get()}`);

    const initialTokenSupply = 50_000n;
    const builder = userClient.initBuilder();
    for (const token of [{ token: token1, symbol: 'TKNA' }, { token: token2, symbol: 'TKNB' }]) {
        // Set token info and metadata with base permission of ACCESS for anyone to use the token
        const basicMetadata = Buffer.from(JSON.stringify({ decimalPlaces: 6 })).toString('base64');
        builder.setInfo({
            name: token.symbol,
            description: `Example Token ${token.symbol}`,
            metadata: basicMetadata,
            defaultPermission: new KeetaAnchor.KeetaNet.lib.Permissions(['ACCESS'])
        }, { account: token.token });

        // Setup Initial Token Supplies and Distribute Amounts to Liquidity Pool
        builder.modifyTokenSupply(initialTokenSupply, { account: token.token });
        // Compute blocks to ensure token supply is added before sending
        await builder.computeBlocks();
        builder.send(liquidityProvider, 10_000n, token.token, undefined, { account: token.token });
    }

    // Compute blocks again before sending to account and liquidity
    await builder.computeBlocks();
    // Send some TKN1 Supply to user account
    builder.send(account, 10_000n, token1, undefined, { account: token1});

    // Send some KTA Base Token to the liquidity provider so it can pay fees
    builder.send(liquidityProvider, 2n * (10n ** BigInt(await getBaseTokenDecimals(network))), userClient.baseToken);

    // Compute Final Blocks
    await builder.computeBlocks();
    console.log(`Token Setup Blocks: ${[builder.blocks.map(block => block.hash.toString())]}`);

    // Publish builder blocks to the network
    await builder.publish();

    // Setup FX Anchor HTTP Server
    await using fxServer = new KeetaNetFXAnchorHTTPServer({
        ...(logger ? { logger: logger } : {}),
		account: liquidityProvider,
        quoteSigner: liquidityProvider,
		client: { client: userClient.client, network: userClient.config.network, networkAlias: userClient.config.networkAlias },
		fx: {
            // Provided function to calculate the rate
			getConversionRateAndFee: async function(request) {
				let rate = 0.88;
                // Affinity could be 'from' or 'to' and can change which direction the rate should be calculated
				if (request.affinity === 'to') {
					rate = 1 / rate;
				}
				return({
					account: liquidityProvider.publicKeyString.get(),
					convertedAmount: (parseInt(request.amount) * rate).toFixed(0),
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
		root: account,
		signer: account,
		account: account,
        ...(logger ? { logger: logger } : {})
    });

    const initialBalances = await userClient.allBalances();
    console.debug(`Initial User Balances: ${util.inspect(debugPrintableObject(initialBalances), { depth: 4, colors: true })}`);

    // getQuotes returns any FX providers that can fulfil the requested swap
    const fxQuoteWithProviders = await fxClient.getQuotes(
        { from: token1, to: token2, amount: 100, affinity: 'from' }
    );
    if (fxQuoteWithProviders === null) {
        throw(new Error('Failed to get FX Providers for Exchange'));
    }

    // In this example there is only 1 FX provider, so pick the first one
    const fxQuoteProvider = fxQuoteWithProviders[0];
    console.debug(`Exchange Quote: ${util.inspect(debugPrintableObject(fxQuoteProvider.quote), { depth: 4, colors: true })}`);

    // Create the swap request block.  Defined amount being sent from our account
    const from = { account: account, token: token1, amount: 100n };

    // Define amount to be received/swapped with the FX provider using the converted amount provided in the quote
    const to = { account: Account.fromPublicKeyString(fxQuoteProvider.quote.account), token: token2, amount: BigInt(fxQuoteProvider.quote.convertedAmount) };

    // Create the swap block.
    // This is optional and will be handled by the FX Client automatically if the block is not provided using the intil quote request parameters
    const createSwapBlock = await userClient.createSwapRequest({ from, to });
    await fxQuoteProvider.createExchange(createSwapBlock);

    const finalBalances = await userClient.allBalances();
    console.debug(`Final User Balances: ${util.inspect(debugPrintableObject(finalBalances), { depth: 4, colors: true })}`);

    const liquidityBalances = await userClient.client.getAllBalances(liquidityProvider);
    console.debug(`Liquidity Provider Final Balances: ${util.inspect(debugPrintableObject(liquidityBalances), { depth: 4, colors: true })}`);
}

main().then(function() {
	process.exit(0);
}, function(err) {
	console.error(err);
	process.exit(1);
});
