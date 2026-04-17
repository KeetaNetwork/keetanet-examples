#! /usr/bin/env ts-node

/*
 * Description: Example of using the Keeta Anchor Client to move USDC from Base Sepolia to Keeta Network
 *
 * This example demonstrates:
 * 1. Creating a persistent forwarding address on Base Sepolia
 * 2. Receiving USDC on Base Sepolia and automatically forwarding to Keeta
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
 * Base Sepolia Testnet Configuration
 * Chain ID: 84532
 * USDC Contract: 0x036CbD53842c5426634e7929541eC2318f3dCF7e
 */
const BASE_SEPOLIA_CHAIN_ID = 84532n;
const KEETA_USDC_ASSET = 'keeta_apna75yhhvnv4ei7ape55hndk4yepno7a7i2mhtiwahiygixjcnmvswxhnmnk';

async function main() {
	console.log('Keeta Asset Movement Example: Base Sepolia USDC => Keeta');

	// Generate a random account for this demo
	const seed = Account.generateRandomSeed({ asString: true });
	const userAccount = Account.fromSeed(seed, 0);

	console.log(`Seed: ${seed}`);
	console.log(`Keeta Account: ${userAccount.publicKeyString.get()}\n`);

	// Create UserClient for the Keeta Test Network
	await using userClient = KeetaAnchor.KeetaNet.UserClient.fromNetwork(network, userAccount);

	// Create Asset Movement Client to handle cross-chain transfers
	const assetMovementClient = new KeetaAnchor.AssetMovement.Client(userClient, {
		root: userClient.networkAddress,
		...logger
	});

	// Find Asset Movement providers that support Base Sepolia => Keeta
	const providers = await assetMovementClient.getProvidersForTransfer({
		// USDC contract address on Base Sepolia (EVM chain)
		asset: KEETA_USDC_ASSET,
		// Source: Base Sepolia (EVM chain with chain ID 84532)
		from: {
			type: 'chain',
			chain: {
				type: 'evm',
				chainId: BASE_SEPOLIA_CHAIN_ID
			}
		},
		// Destination: Keeta Network
		to: {
			type: 'chain',
			chain: {
				type: 'keeta',
				networkId: userClient.network
			}
		}
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
		// USDC contract on Base Sepolia
		asset: KEETA_USDC_ASSET,
		// Source location: Base Sepolia (where funds will be received)
		sourceLocation: {
			type: 'chain',
			chain: {
				type: 'evm',
				chainId: BASE_SEPOLIA_CHAIN_ID
			}
		},
		// Destination location: Keeta Network (where funds will be forwarded)
		destinationLocation: {
			type: 'chain',
			chain: {
				type: 'keeta',
				networkId: userClient.network
			}
		},
		// Destination address: Your Keeta account
		destinationAddress: userAccount.publicKeyString.get()
	});

	if (!persistentAddressResponse) {
		throw(new Error('Failed to create persistent forwarding address'));
	}

	const persistentAddress = persistentAddressResponse;

	// Display the forwarding address
	console.log('========================================');
	console.log(' YOUR BASE SEPOLIA FORWARDING ADDRESS ');
	console.log('========================================');
	console.log(`Address: ${persistentAddress.address}`);
	console.log(`This address will automatically forward USDC received on Base Sepolia`);
	console.log(`to your Keeta account: ${persistentAddress.destinationAddress}`);
	console.log('========================================\n');

	console.log('HOW TO GET TEST USDC:');
	console.log('----------------------------------------');
	console.log('1. Visit Circle\'s Testnet Faucet:');
	console.log('   https://faucet.circle.com/');
	console.log('');
	console.log('2. Select "Base Sepolia" from the network dropdown');
	console.log('');
	console.log('3. Select "USDC" as the token');
	console.log('');
	console.log('4. Enter your forwarding address:');
	console.log(`   ${persistentAddress.address}`);
	console.log('');
	console.log('5. Request test USDC (usually 20 USDC per request)');
	console.log('----------------------------------------');

	// Wait for user confirmation before monitoring transactions
	const shouldMonitor = await promptUser('Would you like to monitor for incoming transactions? (yes/no): ');

	if (shouldMonitor.toLowerCase() === 'yes' || shouldMonitor.toLowerCase() === 'y') {
		console.log('Monitoring for transactions... (This will check every 10 seconds. Press Ctrl+C to stop)');

		// Monitor for completed transaction
		const monitoringInterval = setInterval(async () => {
			try {
				const transactionResponse = await provider.listTransactions({
					persistentAddresses: [{
						location: {
							type: 'chain',
							chain: {
								type: 'evm',
								chainId: BASE_SEPOLIA_CHAIN_ID
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
					console.log(`Current Status: ${tx.status}. Waiting for it to complete...`);
					return;
				}

				console.log(`Completed transaction detected!`);
				console.log(`  ID: ${tx.id}`);
				console.log(`  Status: ${tx.status}`);
				console.log(`  Asset: ${tx.asset}`);
				console.log(`  From: ${tx.from.location}`);
				console.log(`  From Value: ${tx.from.value}`);
				console.log(`  To: ${tx.to.location}`);
				console.log(`  To Value: ${tx.to.value}`);
				console.log(`  Created: ${tx.createdAt}`);
				console.log(`  Updated: ${tx.updatedAt}`);

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
		}, 10000); // Check every 10 seconds

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
