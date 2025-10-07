import * as KeetaNet from '@keetanetwork/keetanet-client';
import type { Account } from '@keetanetwork/keetanet-client/lib/account.js';

const debugPrintableObject = KeetaNet.lib.Utils.Helper.debugPrintableObject;

export async function getBaseTokenDecimals(network: 'test' | 'main'): Promise<number> {
    const userClient = KeetaNet.UserClient.fromNetwork(network, null);
    const baseTokenInfo = await userClient.client.getAccountInfo(userClient.baseToken);
    const tokenMetadata = JSON.parse(Buffer.from(baseTokenInfo.info.metadata, 'base64').toString());
    return(tokenMetadata['decimalPlaces']);
}

export async function getFaucetTokens(acct: Account | string) {
    const account = typeof acct === 'string' ? acct : acct.publicKeyString.get()
    try {
        const params = new URLSearchParams();
        params.append('address', account);
        params.append('amount', '10');
        const response = await fetch("https://faucet.test.keeta.com", {
            method: "POST",
            headers: {
                "Content-Type": "application/x-www-form-urlencoded",
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
}

export async function waitForResult(code: () => Promise<boolean>, timeout = 10000) {
    let result = await code();
    const startTime = Date.now();
    while(!result && (startTime + timeout > Date.now())) {
        await KeetaNet.lib.Utils.Helper.asleep(50);
        result = await code();
    }
    return(result);
}

export {
    debugPrintableObject
};
