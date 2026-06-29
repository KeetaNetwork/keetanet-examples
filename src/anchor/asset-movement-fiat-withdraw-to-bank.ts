#! /usr/bin/env ts-node

/*
 * Description: Example of using the Keeta Anchor client to withdraw USD from Keeta Test Network to a US bank account;
 * This example assumes that KYC has already been completed and onboarding has been performed.
 */

import * as KeetaAnchor from '@keetanetwork/anchor';
import { Errors, type RecipientResolved } from '@keetanetwork/anchor/services/asset-movement/common.js';
import { debugPrintableObject as DPO, formatDecimals, getFaucetTokens, getTokenDecimals, promptUser } from '../helper.js';
import * as util from 'util';

const Account = KeetaAnchor.KeetaNet.lib.Account;

const DEBUG = false;
const logger = DEBUG ? { logger: console } : {};
const network = 'test';

const KEETA_USD_ASSET = Account.fromPublicKeyString('keeta_any4zllibya6fum3lsoimxmnmeo57nklxlh4c6d6xosfacarfaa3knkiprkmm');
const US_BANK_DESTINATION = { type: 'bank-account', account: { type: 'us' }} as const;

async function main() {
	console.log('Keeta Fiat Withdraw Example: Keeta USD => US Bank');
	console.log('===================================================\n');

	const seed = await promptUser('Enter your Keeta SEED with KYC completed: ');
	if (!seed.trim()) {
		throw(new Error('Invalid seed'));
	}
	const account = Account.fromSeed(seed.trim(), 0);

	console.log(`Keeta Account: ${account.publicKeyString.get()}`);
	console.log(`USD Token: ${KEETA_USD_ASSET.publicKeyString.get()}\n`);

	await using userClient = KeetaAnchor.KeetaNet.UserClient.fromNetwork(network, account);

	const usdDecimals = await getTokenDecimals(network, KEETA_USD_ASSET);
	if (usdDecimals === null) {
		throw(new Error('Failed to get USD token decimals'));
	}

	const baseTokenBalance = await userClient.balance(userClient.baseToken);
	if (baseTokenBalance === 0n) {
		const faucetRequest = await getFaucetTokens(account, network);
		if (!faucetRequest) {
			throw(new Error('Failed to get faucet tokens for transaction fees'));
		}
	}

	const currentBalance = await userClient.balance(KEETA_USD_ASSET);
	console.log(`Current USD Balance: ${formatDecimals(currentBalance, usdDecimals)} USD`);

	if (currentBalance === 0n) {
		throw(new Error('You have no USD balance on Keeta Test Network. Deposit USD first (see asset-movement-fiat-deposit.ts).'));
	}

	const amountInput = await promptUser(`How much USD do you want to withdraw? (max ${formatDecimals(currentBalance, usdDecimals)}): `);
	const amountInUsd = parseFloat(amountInput);
	if (isNaN(amountInUsd) || amountInUsd <= 0) {
		throw(new Error('Invalid amount. Enter a positive number.'));
	}

	const amountToWithdraw = BigInt(Math.floor(amountInUsd * (10 ** usdDecimals)));
	if (amountToWithdraw > currentBalance) {
		throw(new Error(`Insufficient balance. You only have ${formatDecimals(currentBalance, usdDecimals)} USD`));
	}

	const accountNumber = (await promptUser('Enter the US bank account number: ')).trim();
	const routingNumber = (await promptUser('Enter the US bank routing number: ')).trim();
	const bankName = (await promptUser('Enter the US bank name: ')).trim();
	const accountType = (await promptUser('Enter the account type (checking or savings): ')).trim().toLowerCase();
	const firstName = (await promptUser('Enter the account holder first name: ')).trim();
	const lastName = (await promptUser('Enter the account holder last name: ')).trim();
	const addressLine1 = (await promptUser('Enter the US account holder address line 1: ')).trim();
	const addressLine2 = (await promptUser('Enter the US account holder address line 2 (optional): ')).trim();
	const city = (await promptUser('Enter the account holder city: ')).trim();
	const subdivision = (await promptUser('Enter the account holder state (2-letter code): ')).trim();
	const postalCode = (await promptUser('Enter the account holder postal code: ')).trim();

	if (!routingNumber || !accountNumber || !bankName || !accountType || !firstName || !lastName || !addressLine1 || !city || !subdivision || !postalCode) {
		throw(new Error('All bank and account holder fields are required'));
	}

	const bankRecipient: RecipientResolved = {
		type: 'bank-account',
		accountType: 'us',
		accountNumber,
		routingNumber,
		// eslint-disable-next-line @typescript-eslint/consistent-type-assertions
		accountTypeDetail: accountType as 'checking' | 'savings',
		accountOwner: {
			type: 'individual',
			firstName,
			lastName
		},
		accountAddress: {
			line1: addressLine1,
			line2: addressLine2,
			city,
			subdivision,
			postalCode,
			country: 'US'
		}
	} as const;

	const assetMovementClient = new KeetaAnchor.AssetMovement.Client(userClient, {
		root: userClient.networkAddress,
		...logger
	});

	const keetaSource = {
		type: 'chain',
		chain: { type: 'keeta', networkId: userClient.network }
	} as const;

	const assetPair = { from: KEETA_USD_ASSET, to: 'USD' as const };

	const providers = await assetMovementClient.getProvidersForTransfer({
		asset: assetPair,
		from: keetaSource,
		to: US_BANK_DESTINATION
	});

	if (!providers || providers.length === 0) {
		throw(new Error('No asset movement providers found for Keeta USD withdrawal to US bank'));
	}

	const provider = providers[0];
	if (!provider) {
		throw(new Error('Provider is undefined'));
	}

	console.log(`\nUsing provider: ${String(provider.providerID)}`);

	const proceed = await promptUser('Proceed with the withdrawal? (y/n): ');
	if (proceed.trim().toLowerCase() !== 'y') {
		throw(new Error('Withdrawal cancelled'));
	}

	let transfer;
	try {
		transfer = await provider.initiateTransfer({
			account,
			asset: assetPair,
			from: { location: keetaSource },
			to: {
				location: US_BANK_DESTINATION,
				recipient: bankRecipient
			},
			value: amountToWithdraw
		});
	} catch (error) {
		if (Errors.KYCShareNeeded.isInstance(error)) {
			console.error('KYC attributes must be shared with the provider before a USD withdrawal can be initiated.');
			console.error('Complete KYC sharing (see kyc-client-sharekyc.ts), then run this example again.');
			return;
		}

		if (Errors.UserActionNeeded.isInstance(error)) {
			console.error('Provider onboarding steps are still required before a USD withdrawal can be initiated.');
			console.error('Complete the actions below (see kyc-client-sharekyc.ts), then run this example again.');
			console.error(util.inspect(DPO(error.actionsNeeded), { depth: 4, colors: true }));
			return;
		}

		throw(error);
	}

	console.log(`\nTransfer initiated with ID: ${transfer.transferID}`);
	console.log('Instructions:', util.inspect(DPO(transfer.instructions), { depth: 4, colors: true }));

	const instruction = transfer.instructions[0];
	if (!instruction || instruction.type !== 'KEETA_SEND') {
		throw(new Error('Expected KEETA_SEND instruction not found'));
	}

	const anchorAccount = Account.toAccount(instruction.sendToAddress);
	const usdTokenAccount = KEETA_USD_ASSET.assertKeyType(Account.AccountKeyAlgorithm.TOKEN);

	if (!instruction.external) {
		throw(new Error('Expected external field data in instruction'));
	}

	console.log('Sending USD to anchor ... please wait ...');

	// Send the required funds to the anchor account with the provided external identifier instructions
	const sendBlockResult = await userClient.send(
		anchorAccount,
		amountToWithdraw,
		usdTokenAccount,
		instruction.external,
		{ generateFeeBlock: userClient.config.generateFeeBlock }
	);

	if (!sendBlockResult.publish || sendBlockResult.from !== 'direct') {
		throw(new Error('Failed to send USD to anchor account'));
	}

	const transferID = transfer.transferID;

	console.log('\nMonitoring transfer status ... (checks every 5 seconds; Ctrl+C to stop)');

	const startTime = Date.now();
	const monitoringInterval = setInterval(async () => {
		try {
			const transactionResult = await transfer.getTransferStatus();

			if (!transactionResult) {
				process.stdout.write('.');
				return;
			}

			const txn = transactionResult.transaction;
			const elapsed = Math.floor((Date.now() - startTime) / 1000);
			console.log(`\n[${elapsed}s] Status: ${txn.status}`);

			if (txn.status === 'PROCESSING' || txn.status === 'COMPLETED') {
				console.log(`
========================================
  WITHDRAWAL PROCESSED SUCCESSFULLY!
========================================
Transfer ID: ${transferID}
Amount: ${formatDecimals(amountToWithdraw, usdDecimals)} USD
From: Keeta Test Network
To: US bank account ending ${accountNumber.slice(-4)}
========================================
`);

				const finalBalance = await userClient.balance(KEETA_USD_ASSET);
				console.log(`Final USD Balance on Keeta: ${formatDecimals(finalBalance, usdDecimals)} USD`);

				clearInterval(monitoringInterval);
				process.exit(0);
			}

			process.stdout.write('.');
		} catch (error) {
			console.error('\nError monitoring transfer:', error);
		}
	}, 5000);

	process.on('SIGINT', () => {
		clearInterval(monitoringInterval);
		console.log('\nMonitoring stopped.');
		console.log(`Transfer ID: ${transferID}`);
		console.log('Check status later with transfer.getTransferStatus().');
		process.exit(0);
	});

	await new Promise(() => {});
}

main().then(function() {
	process.exit(0);
}, function(err: unknown) {
	console.error(err);
	process.exit(1);
});
