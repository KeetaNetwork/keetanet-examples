#! /usr/bin/env ts-node

/*
 * Description: Example of using the Keeta Anchor Client to interact with Fiat Asset Movement for FX Conversions
 * This example assumes that KYC has already been completed and onboarding has been performed.
 *
 * This example demonstrates:
 * 1. Identifying the available conversion paths and plans for given token pairs
 * 2. Executing the conversion plan
 */

import * as KeetaAnchor from '@keetanetwork/anchor';
import { AnchorChaining } from '@keetanetwork/anchor/lib/chaining.js';
import { debugPrintableObject as DPO, promptUser } from '../helper.js';
import * as util from 'util';

const Account = KeetaAnchor.KeetaNet.lib.Account;

const DEBUG = false;
const logger = DEBUG ? { logger: console } : {};
const network = 'test';

/** On-chain Keeta token addresses for USD and EUR on the test network */
const KEETA_USD_ASSET = Account.fromPublicKeyString('keeta_any4zllibya6fum3lsoimxmnmeo57nklxlh4c6d6xosfacarfaa3knkiprkmm');
const KEETA_EUR_ASSET = Account.fromPublicKeyString('keeta_amqsghqea5mv2476c44ahgt7xbawms5z76d7ffbzirkp56t2hn4bahoqljqeg');

/** $2.00 USD in cents */
const CONVERSION_AMOUNT = 200n;

const defaultPhrase = 'bottom alley wash elbow devote believe maximum amount camera way direct globe frost bottom tilt title ship purse always fluid tennis spread lazy track';

async function main() {
	// Prompt for Keeta seed
	const seed = await promptUser('Enter your Keeta SEED (or press Enter for a default seed): ');
	const account = seed.trim()
		? Account.fromSeed(seed, 0)
		: Account.fromSeed(await Account.seedFromPassphrase(defaultPhrase), 0);

	console.log(`Keeta Fiat FX Example: USD => EUR\n`);
	console.log(`Keeta Account: ${account.publicKeyString.get()}`);
	console.log(`USD Token: ${KEETA_USD_ASSET.publicKeyString.get()}`);
	console.log(`EUR Token: ${KEETA_EUR_ASSET.publicKeyString.get()}\n`);

	await using userClient = KeetaAnchor.KeetaNet.UserClient.fromNetwork(network, account);
	const accountUSDBalance = await userClient.balance(KEETA_USD_ASSET);
	if (accountUSDBalance < CONVERSION_AMOUNT) {
		throw(new Error(`Account does not have enough USD to convert. Account has ${accountUSDBalance} USD, but ${CONVERSION_AMOUNT} USD is required`));
	}

	const anchorChaining = new AnchorChaining({
		client: userClient,
		...logger
	});

	const keetaLocation = `chain:keeta:${userClient.network}` as const;

	// `source.value` sets affinity to `from` — convert this much USD into EUR.
	const conversionRequest = {
		source: {
			asset: KEETA_USD_ASSET,
			location: keetaLocation,
			value: CONVERSION_AMOUNT,
			rail: 'KEETA_SEND' as const
		},
		destination: {
			asset: KEETA_EUR_ASSET,
			location: keetaLocation,
			recipient: account.publicKeyString.get(),
			rail: 'KEETA_SEND' as const
		}
	};

	const paths = await anchorChaining.getPaths(conversionRequest);
	if (!paths || paths.length === 0) {
		throw(new Error('No conversion paths found for USD => EUR on the test network'));
	}

	console.log(`Available Conversion Paths: ${util.inspect(DPO(paths.map(path => path.path)), { depth: 6, colors: true })}`);

	const plans = await anchorChaining.getPlans(conversionRequest);
	if (!plans || plans.length === 0) {
		throw(new Error('No conversion plans could be computed for USD => EUR on the test network'));
	}

	const plan = plans[0];
	if (!plan) {
		throw(new Error('Conversion plan is undefined'));
	}

	console.log(`\nConversion Plan (${plan.path.length} steps):`);
	console.log(util.inspect(DPO({
		path: plan.path,
		plan: {
			...plan.plan,
			/* Remove the transfer and provider properties from the steps as it's verbose and not needed for the plan */
			steps: plan.plan.steps.map(function(step) {
				if (step.type === 'assetMovement') {
					// eslint-disable-next-line @typescript-eslint/no-unused-vars
					const { transfer: _transfer, provider: _provider, ...rest } = step;
					return(rest);
				}
				if (step.type === 'forwarded') {
					// eslint-disable-next-line @typescript-eslint/no-unused-vars
					const { provider: _provider, ...rest } = step;
					return(rest);
				}
				return(step);
			})
		}
	}), { depth: 10, colors: true }));

	const proceed = await promptUser('Proceed with the conversion? (y/n): ');
	if (proceed.trim().toLowerCase() !== 'y') {
		throw(new Error('Conversion cancelled'));
	}
	console.log(`Executing conversion plan ... please wait ...`);

	// Execute the conversion plan
	const conversion = await plan.execute();
	if (!conversion) {
		throw(new Error('Conversion failed'));
	}
	console.log(`Conversion result: ${util.inspect(DPO(conversion), { depth: 6, colors: true })}`);
	console.log(`Account balances after conversion: ${util.inspect(DPO(await userClient.allBalances()), { depth: 6, colors: true })}`);
}

main().then(function() {
	process.exit(0);
}, function(err: unknown) {
	console.error(err);
	process.exit(1);
});
