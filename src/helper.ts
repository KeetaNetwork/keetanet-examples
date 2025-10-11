import * as KeetaNet from '@keetanetwork/keetanet-client';
import type { Account, GenericAccount, TokenAddress } from '@keetanetwork/keetanet-client/lib/account.js';
import type { Networks } from '@keetanetwork/keetanet-client/config/index.js';
import type { JSONSerializable } from '@keetanetwork/keetanet-client/lib/utils/conversion.js';

const debugPrintableObject: (input: unknown) => JSONSerializable = KeetaNet.lib.Utils.Helper.debugPrintableObject.bind(KeetaNet.lib.Utils.Helper);

/**
 * Parse the account metadata
 * @param network network alias to use to get account metadata
 * @param token optional account to get metadata for, if not provided it will use the base token for the network
 * @returns JSON parsed metadata
 */
export async function getAccountMetadata(network: Networks, account?: GenericAccount | string): Promise<unknown> {
	await using userClient = KeetaNet.UserClient.fromNetwork(network, null);
	const accountInfo = await userClient.client.getAccountInfo(account ?? userClient.baseToken);
	const metadataBuffer = Buffer.from(accountInfo.info.metadata, 'base64');
	const networkMetadata = KeetaNet.lib.Utils.Helper.bufferToArrayBuffer(metadataBuffer);
	let metadataUncompressed: ArrayBuffer;
	try {
		metadataUncompressed = KeetaNet.lib.Utils.Buffer.ZlibInflate(networkMetadata);
	} catch {
		metadataUncompressed = networkMetadata;
	}
	const metadataBytes = Buffer.from(metadataUncompressed);
	const metadataDecoded: unknown = JSON.parse(metadataBytes.toString('utf-8'));
	return(metadataDecoded);
}

/**
 * Get the decimalPlaces from the token metadata
 * @param network network alias to use to get token metadata
 * @param token optional token account to get metadata for, if not provided it will use the base token for the network
 * @returns token decimal places
 */
export async function getTokenDecimals(network: Networks, token?: TokenAddress | string): Promise<number | null> {
	const tokenMetadata = await getAccountMetadata(network, token);
	if (tokenMetadata && typeof tokenMetadata === 'object' && 'decimalPlaces' in tokenMetadata && (typeof tokenMetadata.decimalPlaces === 'number' || typeof tokenMetadata.decimalPlaces === 'string')) {
		return(Number(tokenMetadata.decimalPlaces));
	}
	return(null);
}

/**
 * Helper function to wait for a condition to be met
 */
export async function waitForResult(code: () => Promise<boolean>, timeout = 10000): Promise<boolean> {
	for (const startTime = Date.now(); startTime + timeout > Date.now();) {
		const result = await code();
		if (result) {
			return(true);
		}
		await KeetaNet.lib.Utils.Helper.asleep(50);
	}
	return(false);
}

/**
 * Helper function to get tokens from the faucet on the TEST network
 * @param acct account to send faucet tokens too
 * @param network network to use, currently only TEST is supported
 * @returns boolean true/false if it succeeded.
 */
export async function getFaucetTokens(acct: Account | string, network: Networks): Promise<boolean> {
	if (network !== 'test') {
		throw(new Error('Faucet is Only Available on the Test Network'));
	}

	let account: string;
	if (typeof acct === 'string') {
		account = acct;
	} else {
		account = acct.publicKeyString.get();
	}

	await using tempUserClient = KeetaNet.UserClient.fromNetwork(network, null);
	const initialBalance = await tempUserClient.client.getBalance(account, tempUserClient.baseToken);

	// Make a request to the Faucet for KTA tokens
	try {
		const params = new URLSearchParams();
		params.append('address', account);
		params.append('amount', '5');
		const response = await fetch("https://faucet.test.keeta.com", {
			method: "POST",
			headers: {
				"Content-Type": "application/x-www-form-urlencoded"
			},
			body: params.toString()
		});

		if (response.ok) {
			console.log(`Requesting Tokens from Faucet for: ${account}`);
		} else {
			console.error(`Faucet Request Failed for: ${account}`);
		}
	} catch (error) {
		console.error(`Faucet Request Failed for: ${account} `, error);
	}

	// Wait for the account balance to be updated
	const balanceResult = await waitForResult(async function() {
		const balance = await tempUserClient.client.getBalance(account, tempUserClient.baseToken);
		return(balance >= (initialBalance + 5n));
	});

	return(balanceResult);
}

export {
	debugPrintableObject
};
