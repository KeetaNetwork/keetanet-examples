import * as KeetaNet from '@keetanetwork/keetanet-client';
import type { Account } from '@keetanetwork/keetanet-client/lib/account.js';
import type { Networks } from '@keetanetwork/keetanet-client/config/index.js';
import type { JSONSerializable } from '@keetanetwork/keetanet-client/lib/utils/conversion.js';

const debugPrintableObject: (input: unknown) => JSONSerializable = KeetaNet.lib.Utils.Helper.debugPrintableObject.bind(KeetaNet.lib.Utils.Helper);

export async function getBaseTokenDecimals(network: Networks): Promise<number | null> {
	const userClient = KeetaNet.UserClient.fromNetwork(network, null);
	const baseTokenInfo = await userClient.client.getAccountInfo(userClient.baseToken);
	const tokenMetadata: unknown = JSON.parse(Buffer.from(baseTokenInfo.info.metadata, 'base64').toString());
	if (tokenMetadata && typeof tokenMetadata === 'object' && 'decimalPlaces' in tokenMetadata && typeof tokenMetadata.decimalPlaces === 'number') {
		return(tokenMetadata.decimalPlaces)
	}
	return(null);
}

export async function waitForResult(code: () => Promise<boolean>, timeout = 10000) {
	for (const startTime = Date.now(); startTime + timeout > Date.now();) {
		const result = await code();
		if (result) {
			return(true);
		}
		await KeetaNet.lib.Utils.Helper.asleep(50);
	}
	return(false);
}

export async function getFaucetTokens(acct: Account | string, network: Networks) {
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
