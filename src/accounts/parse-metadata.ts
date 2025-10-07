#! /usr/bin/env ts-node

/*
 * Description: Example of using the Keeta Net Client to get Account Metadata.
 */

import * as KeetaNet from '@keetanetwork/keetanet-client';
import { debugPrintableObject } from '../helper.js';

const DPO = debugPrintableObject;

async function main() {
	const networkAlias = 'test';
	const config = KeetaNet.Client.Config.getDefaultConfig(networkAlias);
	const userClient = KeetaNet.UserClient.fromNetwork(networkAlias, null);
	const networkAddress = userClient.networkAddress;

	const networkInfo = await userClient.client.getAccountInfo(networkAddress);
	const metadataBuffer = Buffer.from(networkInfo.info.metadata, 'base64');
	const networkMetadata = KeetaNet.lib.Utils.Helper.bufferToArrayBuffer(metadataBuffer);
	let metadataUncompressed: ArrayBuffer;
	try {
		metadataUncompressed = KeetaNet.lib.Utils.Buffer.ZlibInflate(networkMetadata);
	} catch {
		metadataUncompressed = networkMetadata;
	}
	const metadataBytes = Buffer.from(metadataUncompressed);
	const metadataDecoded: unknown = JSON.parse(metadataBytes.toString('utf-8'));

	console.debug('Network Alias:', networkAlias);
	console.debug('Network ID:', config.network);
	console.debug('Network Account:', networkAddress.publicKeyString.get());
	console.debug('Network Account Metadata', DPO(metadataDecoded));
}

main().then(function() {
	process.exit(0);
}, function(err: unknown) {
	console.error(err);
	process.exit(1);
});
