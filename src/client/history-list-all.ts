#! /usr/bin/env ts-node

/*
 * Description: Example of using the Keeta Network Client to get recent transaction history.
 */

import * as KeetaNet from '@keetanetwork/keetanet-client';
import { debugPrintableObject as DPO } from '../helper.js';
import * as util from 'node:util';

const network = 'test';

async function main() {
    // We just need a Client and not a UserClient in this case to list all history
	const client = KeetaNet.Client.fromNetwork(network);
	
    // History is paginated so we use `depth` and `startBlocksHash` to paginate results
    const latest10History = await client.getHistory(null, { depth: 10 });

    // Print the results (latest 10 VoteStaple's)
	console.debug(util.inspect(DPO(latest10History), { depth: 10, colors: true }));
}

main().then(function() {
	process.exit(0);
}, function(err: unknown) {
	console.error(err);
	process.exit(1);
});
