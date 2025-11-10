#! /usr/bin/env ts-node

/*
 * Description: Example of using the Keeta Network Client to send tokens to another account.
 */

import * as KeetaNet from '@keetanetwork/keetanet-client';
import { debugPrintableObject as DPO, getFaucetTokens } from '../helper.js';
import * as util from 'util';

const Account = KeetaNet.lib.Account;
const network = 'test';

async function main() {
	const seed = Account.generateRandomSeed({ asString: true });
	const userAccount = Account.fromSeed(seed, 0);
	const recipient = Account.toAccount('keeta_aabqdjl2ys7rh7zucixerbjb57x5vctdqwsheuv74qjswjresjzuekc3fp26ejq');
	const tkna = 'keeta_anqmr5runcgsn3fw2ltzwy42skrwkel7ldpemucjnmmr2ddpjlvdoowumr2z4';

	// Create UserClient for Network and User Account
	await using userClient = KeetaNet.UserClient.fromNetwork(network, userAccount);

	await userClient.send(recipient, 100000n, tkna);
}

main().then(function() {
	process.exit(0);
}, function(err: unknown) {
	console.error(err);
	process.exit(1);
});
