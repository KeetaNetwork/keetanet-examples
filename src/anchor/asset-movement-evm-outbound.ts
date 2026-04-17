#! /usr/bin/env ts-node

/*
 * Description: Example of using the Keeta Anchor Client to move USDC from Keeta Test Network to Base Sepolia
 *
 * This example demonstrates:
 * 1. Initiating an outbound transfer from Keeta to Base Sepolia
 * 2. Constructing a block with external field data for the bridge recipient
 * 3. Publishing the transaction using UserClient
 * 4. Monitoring the transfer status
 *
 * Prerequisites:
 * - Run the asset-movement-evm-inbound.ts example first to obtain USDC tokens on Keeta Test Network
 * - Have a Base Sepolia wallet address ready to receive the funds
 */

import * as KeetaAnchor from '@keetanetwork/anchor';
import { debugPrintableObject as DPO, formatDecimals, getFaucetTokens, promptUser } from '../helper.js';
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
const USDC_DECIMALS = 6;

async function main() {
	console.log('Keeta Asset Movement Example: Keeta => Base Sepolia USDC');
	console.log('=========================================================');

	console.log('IMPORTANT: Before running this example:');
	console.log('1. Run asset-movement-evm-inbound.ts to receive USDC tokens on Keeta Test Network');
	console.log('2. Ensure you have sufficient USDC balance on Keeta to send\n');

	// Prompt for Keeta seed
	const seed = await promptUser('Enter your Keeta SEED (or press Enter for new random seed): ');
	const userAccount = seed.trim()
		? Account.fromSeed(seed.trim(), 0)
		: Account.fromSeed(Account.generateRandomSeed({ asString: true }), 0);

	console.log(`Keeta Account: ${userAccount.publicKeyString.get()}`);

	// Prompt for Base Sepolia recipient address
	const baseRecipientAddress = await promptUser('Enter the Base Sepolia wallet address to send USDC to: ');

	if (!baseRecipientAddress || baseRecipientAddress.length !== 42 || !baseRecipientAddress.startsWith('0x')) {
		throw(new Error('Invalid Base Sepolia address. Must be a valid Ethereum address (0x...)'));
	}

	// Create UserClient for the Keeta Test Network
	await using userClient = KeetaAnchor.KeetaNet.UserClient.fromNetwork(network, userAccount);

	// Get funds from the faucet if user account has 0 KTA for fees
	const baseTokenBalance = await userClient.balance(userClient.baseToken);
	if (baseTokenBalance === 0n) {
		// Get Tokens from the Faucet for Fees
		const faucetRequest = await getFaucetTokens(userAccount, network);

		if (!faucetRequest) {
			throw(new Error('Failed to get Faucet Tokens'));
		}
	}

	// Check current USDC balance on Keeta
	const usdcTokenAccount = Account.toAccount(KEETA_USDC_ASSET);
	if (!usdcTokenAccount.isToken()) {
		throw(new Error('USDC Token Account is not a valid token'));
	}

	const currentBalance = await userClient.balance(usdcTokenAccount);
	console.log(`\nCurrent USDC Balance: ${currentBalance} (${formatDecimals(currentBalance, USDC_DECIMALS)} USDC)`);

	if (currentBalance === 0n) {
		throw(new Error('You have no USDC balance on Keeta Test Network. Please run asset-movement-evm-inbound.ts first to get USDC tokens.'));
	}

	// Prompt for amount to send
	const amountInput = await promptUser(`How much USDC do you want to send? (in USDC, max ${formatDecimals(currentBalance, USDC_DECIMALS)}): `);
	const amountInUSDC = parseFloat(amountInput);

	if (isNaN(amountInUSDC) || amountInUSDC <= 0) {
		throw(new Error('Invalid amount. Please enter a positive number.'));
	}

	const amountToSend = BigInt(Math.floor(amountInUSDC * (10 ** USDC_DECIMALS))); // convert to raw amount

	if (amountToSend > currentBalance) {
		throw(new Error(`Insufficient balance. You only have ${formatDecimals(currentBalance, USDC_DECIMALS)} USDC`));
	}

	// Create Asset Movement Client to handle cross-chain transfers
	const assetMovementClient = new KeetaAnchor.AssetMovement.Client(userClient, {
		root: userClient.networkAddress,
		...logger
	});

	// Find Asset Movement providers that support Keeta => Base Sepolia
	const providers = await assetMovementClient.getProvidersForTransfer({
		asset: KEETA_USDC_ASSET,
		// Source: Keeta Network
		from: {
			type: 'chain',
			chain: {
				type: 'keeta',
				networkId: userClient.network
			}
		},
		// Destination: Base Sepolia (EVM chain with chain ID 84532)
		to: {
			type: 'chain',
			chain: {
				type: 'evm',
				chainId: BASE_SEPOLIA_CHAIN_ID
			}
		}
	});

	if (!providers || providers.length === 0) {
		throw(new Error('No Asset Movement providers found for Keeta => Base Sepolia. Please ensure an Asset Movement anchor is configured to support this transfer.'));
	}

	// Use the first provider for this example
	const provider = providers[0];
	if (!provider) {
		throw(new Error('Provider is undefined'));
	}

	// Initiate the outbound transfer from Keeta to Base Sepolia
	const initiateResponse = await provider.initiateTransfer({
		asset: KEETA_USDC_ASSET,
		from: {
			location: {
				type: 'chain',
				chain: {
					type: 'keeta',
					networkId: userClient.network
				}
			}
		},
		to: {
			location: {
				type: 'chain',
				chain: {
					type: 'evm',
					chainId: BASE_SEPOLIA_CHAIN_ID
				}
			},
			recipient: baseRecipientAddress
		},
		value: amountToSend
	});

	if (!initiateResponse) {
		throw(new Error('Failed to initiate transfer'));
	}

	console.log(`\nTransfer initiated with ID: ${initiateResponse.transferId}`);
	console.log('Instructions:', util.inspect(DPO(initiateResponse.instructions), { depth: 4, colors: true }));

	// Get the bridge holding account from the instructions
	const instruction = initiateResponse.instructions[0];
	if (!instruction || instruction.type !== 'KEETA_SEND') {
		throw(new Error('Expected KEETA_SEND instruction not found'));
	}

	const anchorAccount = Account.toAccount(instruction.sendToAddress);

	if (!instruction.external) {
		throw(new Error('Expected external field data in instruction'));
	}

	// Send the specified amount of USDC to the anchor account with the external field data from the instructions
	// External field data tells the anchor the details for the transfer
	const sendBlockResult = await userClient.send(anchorAccount, amountToSend, usdcTokenAccount, instruction.external, { generateFeeBlock: userClient.config.generateFeeBlock });

	if (!sendBlockResult.publish || sendBlockResult.from !== 'direct') {
		throw(new Error('Failed to send block to anchor account'));
	}

	// Monitor the transfer status
	console.log('\nMonitoring transfer status... (This will check every 5 seconds. Press Ctrl+C to stop)');

	const startTime = Date.now();
	const monitoringInterval = setInterval(async () => {
		try {
			const transactionResult = await provider.getTransferStatus({ id: initiateResponse.transferId });

			if (!transactionResult) {
				process.stdout.write('.');
				return;
			}

			const txn = transactionResult.transaction;

			const elapsed = Math.floor((Date.now() - startTime) / 1000);
			console.log(`\n[${elapsed}s] Status: ${txn.status}`);

			if (txn.status === 'COMPLETE') {
				console.log('\n========================================');
				console.log('  TRANSFER COMPLETED SUCCESSFULLY! ');
				console.log('========================================');
				console.log(`Transfer ID: ${initiateResponse.transferId}`);
				console.log(`Amount: ${formatDecimals(amountToSend, USDC_DECIMALS)} USDC`);
				console.log(`From: Keeta Test Network`);
				console.log(`To: Base Sepolia (${baseRecipientAddress})`);
				console.log('========================================\n');

				// Check final Keeta balance
				const finalBalance = await userClient.client.getBalance(userAccount, usdcTokenAccount);
				console.log(`Final USDC Balance on Keeta: ${formatDecimals(finalBalance, USDC_DECIMALS)} USDC`);

				clearInterval(monitoringInterval);
				process.exit(0);
			} else {
				process.stdout.write('.');
			}
		} catch (error) {
			console.error('\nError monitoring transfer:', error);
		}
	}, 5000); // Check every 5 seconds

	// Handle Ctrl+C gracefully
	process.on('SIGINT', () => {
		clearInterval(monitoringInterval);
		console.log('\nMonitoring stopped.');
		console.log(`Transfer ID: ${initiateResponse.transferId}`);
		console.log('You can check the status later using the provider.getTransferStatus() method.');
		process.exit(0);
	});

	// Keep the script running
	await new Promise(() => {}); // Run indefinitely until Ctrl+C
}

main().then(function() {
	process.exit(0);
}, function(err: unknown) {
	console.error(err);
	process.exit(1);
});
