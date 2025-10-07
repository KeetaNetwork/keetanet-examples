import * as KeetaNet from '@keetanetwork/keetanet-client';
import type { JSONSerializable } from '@keetanetwork/keetanet-client/lib/utils/conversion.js';

const debugPrintableObject: (input: unknown) => JSONSerializable = KeetaNet.lib.Utils.Helper.debugPrintableObject.bind(KeetaNet.lib.Utils.Helper);

export {
	debugPrintableObject
};
