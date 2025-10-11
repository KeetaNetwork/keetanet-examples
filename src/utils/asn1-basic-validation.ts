#! /usr/bin/env ts-node

/*
 * Description: Example of using the Keeta Network Client to Perform ASN.1 validation
 */

import * as KeetaNet from '@keetanetwork/keetanet-client';
import type { ValidateASN1 } from '@keetanetwork/keetanet-client/lib/utils/asn1.js';

const schema: [
	name: typeof KeetaNet.lib.Utils.ASN1.ValidateASN1.IsString,
	old: typeof KeetaNet.lib.Utils.ASN1.ValidateASN1.IsBoolean,
	lives: typeof KeetaNet.lib.Utils.ASN1.ValidateASN1.IsInteger,
	dateOfBirth: { optional: typeof KeetaNet.lib.Utils.ASN1.ValidateASN1.IsDate }
] = [
	KeetaNet.lib.Utils.ASN1.ValidateASN1.IsString,
	KeetaNet.lib.Utils.ASN1.ValidateASN1.IsBoolean,
	KeetaNet.lib.Utils.ASN1.ValidateASN1.IsInteger,
	{ optional: KeetaNet.lib.Utils.ASN1.ValidateASN1.IsDate }
] satisfies Parameters<typeof KeetaNet.lib['Utils']['ASN1']['ValidateASN1']['againstSchema']>[1];

const input: ValidateASN1.SchemaMap<typeof schema> = [
	'Roger',
	true,
	13n,
	undefined
];

// Create an ASN.1 object and convert it to DER format
const dataObject = KeetaNet.lib.Utils.ASN1.JStoASN1(input);
const data = dataObject.toBER();

// Method 1
const decodedObject  = new KeetaNet.lib.Utils.ASN1.BufferStorageASN1(data, schema);
const decoded1 = decodedObject.getASN1();
console.log('Method 1:');
console.log('Input  :', input);
console.log('DER    :', decodedObject.getDERBuffer().toString('base64'));
console.log('Decoded:', decoded1);
console.log('');

// Method 2
const decodedJS = KeetaNet.lib.Utils.ASN1.ASN1toJS(data);
const decoded2 = KeetaNet.lib.Utils.ASN1.ValidateASN1.againstSchema(decodedJS, schema);
console.log('Method 2:');
console.log('Input  :', input);
console.log('Decoded:', decoded2);
