#! /usr/bin/env ts-node

/*
 * Description: Example of using the Keeta Network Client to Create and Swap using an Intermediary.
 */

import * as KeetaNet from '@keetanetwork/keetanet-client';
import { debugPrintableObject as DPO, getFaucetTokens } from '../helper.js';
import * as util from 'util';

const Account = KeetaNet.lib.Account;
const network = 'test';

async function main() {
	// Generate a random seed and user accounts using different KeyAlgorithm
	const seed = Account.generateRandomSeed({ asString: true });
	const user1 = Account.fromSeed(seed, 0);
	const user2 = Account.fromSeed(seed, 1);
	const escrow = Account.fromSeed(seed, 2);

	console.log(`User Seed: ${seed}`);
	console.log(`User1 Account: ${user1.publicKeyString.get()}`);
	console.log(`User2 Account: ${user2.publicKeyString.get()}`);
	console.log(`Escrow Account: ${escrow.publicKeyString.get()}`);

	// Create UserClient for Network and User Account
	await using user1Client = KeetaNet.UserClient.fromNetwork(network, user1);
	await using user2Client = KeetaNet.UserClient.fromNetwork(network, user2);
	await using escrowClient = KeetaNet.UserClient.fromNetwork(network, escrow);

	// Get Tokens from the Faucet for Fees
	const faucetRequests = await Promise.all([
		getFaucetTokens(user1, network),
		getFaucetTokens(user2, network),
		getFaucetTokens(escrow, network)
	]);

	if (faucetRequests.filter(Boolean).length !== 3) {
		throw(new Error('Failed to get Faucet Tokens'));
	}

	// Create a Token on the Network
	const { account: tokenA } = await user1Client.generateIdentifier(Account.AccountKeyAlgorithm.TOKEN);
	const { account: tokenB } = await user2Client.generateIdentifier(Account.AccountKeyAlgorithm.TOKEN);
	if (!tokenA.isToken() || !tokenB.isToken()) {
		// basic type narrowing for AccountKeyAlgorithm.TOKEN
		throw(new Error('Tokens Should be TOKEN Key Algorithm'));
	}
	for (const user of [{ client: user1Client, token: tokenA, name: 'ESCROWA' },{ client: user2Client, token: tokenB, name: 'ESCROWB' }]) {
		const builder = user.client.initBuilder();
		builder.modifyTokenSupply(100000n, { account: user.token });
		await builder.computeBlocks();
		builder.setInfo({
			name: user.name, description: `Token Escrow ${user.name}`, defaultPermission: new KeetaNet.lib.Permissions(['ACCESS']), metadata: ''
		}, { account: user.token });
		builder.send(user.client.account, 100n, user.token, undefined, { account: user.token });
		builder.send(escrow, 20n, user.token, undefined, { account: user.token });
		await builder.publish();
	}

	const builder1 = user1Client.initBuilder();
	builder1.send(escrow, 15n, tokenA);
	builder1.receive(escrow, 10n, tokenB, true);
	const user1Blocks = await builder1.computeBlocks();

	const builder2 = user2Client.initBuilder();
	builder2.send(escrow, 10n, tokenB);
	builder2.receive(escrow, 15n, tokenA, true);
	const user2Blocks = await builder2.computeBlocks();

	const escrowBuilder = escrowClient.initBuilder();
	escrowBuilder.send(user2, 15n, tokenA);
	escrowBuilder.send(user1, 10n, tokenB);
	const escrowBlocks = await escrowBuilder.computeBlocks();

	const blocks = [...escrowBlocks.blocks, ...user1Blocks.blocks, ...user2Blocks.blocks];
	await escrowClient.transmit(blocks);

	console.log(`ESC1 Token: ${tokenA.publicKeyString.get()}`);
	console.log(`ESC2 Token: ${tokenB.publicKeyString.get()}`);
	console.log(`SWAP Blocks: ${[blocks.map(block => block.hash.toString())]}`);

	for (const account of [user1, user2, escrow]) {
		const balance = await user1Client.client.getAllBalances(account);
		console.log(`${account.publicKeyString.get()} Balance: ${util.inspect(DPO(balance), { depth: 4, colors: true })}`);
	}
}

main().then(function() {
	process.exit(0);
}, function(err: unknown) {
	console.error(err);
	process.exit(1);
});
