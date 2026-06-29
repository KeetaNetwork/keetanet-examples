#! /usr/bin/env ts-node

/*
 * Description: Example of using the Keeta Anchor Client to create a persistent forwarding address
 */
import * as KeetaAnchor from '@keetanetwork/anchor';

const Account = KeetaAnchor.KeetaNet.lib.Account;

const DEBUG = false;
const logger = DEBUG ? { logger: console } : {};
const network = 'test';

const BASE_SEPOLIA_CHAIN_ID = 84532n;
const KEETA_USDC_ASSET = 'keeta_apna75yhhvnv4ei7ape55hndk4yepno7a7i2mhtiwahiygixjcnmvswxhnmnk';

async function main() {
	// Generate a random account for this demo
	const seed = Account.generateRandomSeed({ asString: true });
	const userAccount = Account.fromSeed(seed, 0);

	console.log(`Seed: ${seed}`);
	console.log(`Keeta Account: ${userAccount.publicKeyString.get()}`);

	// Create UserClient for the Keeta Test Network
	await using userClient = KeetaAnchor.KeetaNet.UserClient.fromNetwork(network, userAccount);

	// Create Asset Movement Client to handle cross-chain transfers
	const assetMovementClient = new KeetaAnchor.AssetMovement.Client(userClient, {
		// default anchor root resolver address, can be customized to connect to a specific anchor
		root: userClient.networkAddress,
		...logger
	});

	// Step 1: Identify a provider for the locations
	const providers = await assetMovementClient.getProvidersForTransfer({
		// USDC Token on Keeta Network
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
	});

	if (!providers || providers.length === 0) {
		throw(new Error('No Providers found'));
	}

	// Use the DEV2 provider which does not require KYC
	const provider = providers.find((p) => p.providerID.toString() === 'DEV2');
	if (!provider) {
		throw(new Error('Provider is undefined'));
	}

	// Create a persistent forwarding address on Base Sepolia that will
	// automatically forward received USDC to your Keeta account
	const persistentAddressResponse = await provider.createPersistentForwardingAddress({
		account: userAccount,
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

	console.log('Persistent address:', persistentAddressResponse.address);
	console.log('Forward to Keeta account:', userAccount.publicKeyString.get());

	// You can now share this address and receive USDC on Base
	// which will be automatically forwarded to your Keeta account
}

main().then(function() {
	process.exit(0);
}, function(err: unknown) {
	console.error(err);
	process.exit(1);
});
