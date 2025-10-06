#! /usr/bin/env ts-node

/*
 * Description: Example of processing Network Permissions using KeetaNet Client.
 */

import * as KeetaNet from '@keetanetwork/keetanet-client';
import { debugPrintableObject } from '../helper.js';

async function main() {
	const networkAlias = 'test';
	const client = KeetaNet.Client.fromNetwork(networkAlias);
	const config = KeetaNet.Client.Config.getDefaultConfig(networkAlias);
	const networkAccount = KeetaNet.lib.Account.generateNetworkAddress(config.network);
	const networkAccountInfo = await client.getAccountInfo(networkAccount);

	console.debug('Network Alias:', networkAlias);
	console.debug('Network ID:', config.network);
	console.debug('Network Account:', networkAccount.publicKeyString.get());
	console.debug('Network Account Info:', debugPrintableObject(networkAccountInfo));
	console.debug('Network Account Default Permissions:', networkAccountInfo.info.defaultPermission?.base.flags);
}

main().then(function() {
	process.exit(0);
}, function(err) {
	console.error(err);
	process.exit(1);
});
