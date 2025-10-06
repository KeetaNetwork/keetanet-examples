#! /usr/bin/env ts-node

/*
 * Description: Example of using the Keeta Anchor Resolver to get metadata.
 */

import * as KeetaAnchor from '@keetanetwork/anchor';
import * as util from 'util';

async function main() {
    const networkAlias = 'test';
    const config = KeetaAnchor.KeetaNet.Client.Config.getDefaultConfig(networkAlias);
    const userClient = KeetaAnchor.KeetaNet.UserClient.fromNetwork(networkAlias, null);
    const networkAddress = userClient.networkAddress;

    const resolver = new KeetaAnchor.lib.Resolver({
        root: networkAddress,
        client: userClient,
        trustedCAs: []
    });

    const metadata = await resolver.getRootMetadata();
    const resolvedMetadata = await KeetaAnchor.lib.Resolver.Metadata.fullyResolveValuizable(metadata);

	console.debug('Network Alias:', networkAlias);
	console.debug('Network ID:', config.network);
	console.debug('Network Account:', networkAddress.publicKeyString.get());
	console.log(util.inspect(resolvedMetadata, { depth: 10, colors: true }));
}

main().then(function() {
	process.exit(0);
}, function(err) {
	console.error(err);
	process.exit(1);
});
