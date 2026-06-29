#! /usr/bin/env ts-node

/*
* Description: Example of using the Keeta Anchor Client to move USDC from Arbitrum Sepolia to Keeta Network
 *
 * This example demonstrates:
 * 1. Creating a persistent forwarding address on Arbitrum Sepolia
 * 2. Receiving USDC on Arbitrum Sepolia and automatically forwarding to Keeta as USD
 * 3. Monitoring transactions
 */
/* eslint-disable @typescript-eslint/no-base-to-string */
import * as KeetaAnchor from '@keetanetwork/anchor';
import { debugPrintableObject as DPO, promptUser } from '../helper.js';
import * as util from 'util';

const Account = KeetaAnchor.KeetaNet.lib.Account;

const DEBUG = false;
const logger = DEBUG ? { logger: console } : {};
const network = 'test';

/**
 * Arbitrum Sepolia Testnet Configuration
 * Chain ID: 421614
 * USDC Contract: 0x75faf114eafb1BDbe2F0316DF893fd58CE46AA4d
 */
const ARBITRUM_SEPOLIA_CHAIN_ID = 421614n;

// Source asset on the EVM chain. EVM stablecoin forwarding requires the source
// asset to be an `evm:0x...` contract address, not a Keeta asset.
const ARBITRUM_SEPOLIA_USDC_ASSET = 'evm:0x75faf114eafb1BDbe2F0316DF893fd58CE46AA4d';

const KEETA_TEST_USD_ASSET = Account.fromPublicKeyString('keeta_any4zllibya6fum3lsoimxmnmeo57nklxlh4c6d6xosfacarfaa3knkiprkmm');

// USDC (on Arbitrum Sepolia) => USD (on Keeta) is a conversion, so the asset is a pair.
const ASSET_PAIR = { from: ARBITRUM_SEPOLIA_USDC_ASSET, to: KEETA_TEST_USD_ASSET } as const;

const defaultPhrase = 'bottom alley wash elbow devote believe maximum amount camera way direct globe frost bottom tilt title ship purse always fluid tennis spread lazy track';

async function main() {
	console.log('Keeta Asset Movement Example: Arbitrum Sepolia USDC => Keeta USD');

	// Prompt for Keeta seed. The account must already have completed KYC, as the
	// asset movement provider requires KYC before it will issue a forwarding address.
	const seed = await promptUser('Enter your Keeta SEED with KYC completed (or press Enter for a default seed): ');
	const userAccount = seed.trim()
		? Account.fromSeed(seed, 0)
		: Account.fromSeed(await Account.seedFromPassphrase(defaultPhrase), 0);

	console.log(`Keeta Account: ${userAccount.publicKeyString.get()}\n`);

	// Create UserClient for the Keeta Test Network
	await using userClient = KeetaAnchor.KeetaNet.UserClient.fromNetwork(network, userAccount);

	// Create Asset Movement Client to handle cross-chain transfers
	const assetMovementClient = new KeetaAnchor.AssetMovement.Client(userClient, {
		// default anchor root resolver address, can be customized to connect to a specific anchor
		root: userClient.networkAddress,
		...logger
	});

	const keetaDestination = {
		type: 'chain',
		chain: { type: 'keeta', networkId: userClient.network }
	} as const;

	// Find Asset Movement providers that support Arbitrum Sepolia => Keeta USD
	const providers = await assetMovementClient.getProvidersForTransfer({
		// USDC (Arbitrum Sepolia) => USD (Keeta)
		asset: ASSET_PAIR,
		// Source: Arbitrum Sepolia (EVM chain with chain ID 421614)
		from: {
			type: 'chain',
			chain: {
				type: 'evm',
				chainId: ARBITRUM_SEPOLIA_CHAIN_ID
			}
		},
		// Destination: Keeta Network
		to: keetaDestination
		// Note: Rail (like 'EVM_CALL', 'EVM_SEND') is determined by the provider's supported operations
	});

	if (!providers || providers.length === 0) {
		throw(new Error('No Asset Movement providers found. This example requires an Asset Movement anchor to be configured.'));
	}

	// Use the first provider for this example
	const provider = providers[0];
	if (!provider) {
		throw(new Error('Provider is undefined'));
	}

	// Create a persistent forwarding address on Base Sepolia that will
	// automatically forward received USDC to your Keeta account
	const persistentAddressResponse = await provider.createPersistentForwardingAddress({
		account: userAccount,
		asset: ASSET_PAIR,
		sourceLocation: {
			type: 'chain',
			chain: { type: 'evm', chainId: ARBITRUM_SEPOLIA_CHAIN_ID }
		},
		destinationLocation: keetaDestination,
		destinationAddress: userAccount.publicKeyString.get()
	});

	if (!persistentAddressResponse) {
		throw(new Error('No provider could create a persistent forwarding address'));
	}

	const persistentAddress = persistentAddressResponse;

	// Display the forwarding address
	console.log(`
========================================
 YOUR ARBITRUM SEPOLIA FORWARDING ADDRESS
========================================
Persistent Address: ${persistentAddress.address}
This address will automatically forward USDC received on Arbitrum Sepolia
to your Keeta account: ${userAccount.publicKeyString.get()}
========================================

HOW TO GET TEST USDC:
----------------------------------------
1. Visit Circle's Testnet Faucet:
   https://faucet.circle.com/

2. Select "Arbitrum Sepolia" from the network dropdown

3. Select "USDC" as the token

4. Enter your forwarding address:
   ${persistentAddress.address}

5. Request test USDC (usually 20 USDC per request)
----------------------------------------
`);

	// Wait for user confirmation before monitoring transactions
	const shouldMonitor = await promptUser('Would you like to monitor for incoming transactions? (yes/no): ');

	if (['yes', 'y'].includes(shouldMonitor.toLowerCase())) {
		console.log('Monitoring for transactions... (This will check every 5 seconds. Press Ctrl+C to stop)');

		// Monitor for completed transaction
		const monitoringInterval = setInterval(async () => {
			try {
				const transactionResponse = await provider.listTransactions({
					account: userAccount,
					persistentAddresses: [{
						location: {
							type: 'chain',
							chain: {
								type: 'evm',
								chainId: ARBITRUM_SEPOLIA_CHAIN_ID
							}
						},
						persistentAddress: persistentAddress.address.toString()
					}]
				});

				if (!transactionResponse) {
					return;
				}

				const tx = transactionResponse.transactions[0];

				if (!tx) {
					process.stdout.write('.');
					return;
				}

				if (tx.status !== 'COMPLETE') {
					return;
				}

				console.log(`\n
Completed transaction detected!
 ID: ${tx.id}
 Status: ${tx.status}
 Asset: ${tx.asset}
 From: ${tx.from.location}
 From Value: ${tx.from.value}
 To: ${tx.to.location}
 To Value: ${tx.to.value}
 Created: ${tx.createdAt}
 Updated: ${tx.updatedAt}
`);

				// Check final Keeta balances
				const balances = await userClient.allBalances();
				console.log('Current Keeta Balances:');
				console.log(util.inspect(DPO(balances), { depth: 4, colors: true }));

				// Transaction complete, stop monitoring and exit
				clearInterval(monitoringInterval);
				console.log('Transaction completed successfully. Exiting...');
				process.exit(0);

			} catch (error) {
				console.error('Error monitoring transactions:', error);
			}
		}, 5000); // Check every 5 seconds

		// Handle Ctrl+C gracefully
		process.on('SIGINT', () => {
			clearInterval(monitoringInterval);
			console.log('Monitoring stopped.');
			process.exit(0);
		});

		// Keep the script running
		await new Promise(() => {}); // Run indefinitely until Ctrl+C
	} else {
		console.log('Example completed!');
	}
}

main().then(function() {
	process.exit(0);
}, function(err: unknown) {
	console.error(err);
	process.exit(1);
});
