#! /usr/bin/env ts-node

/*
 * Description: Use the Keeta Anchor Client to share KYC attributes to an Anchor and Onboard
 */

import * as KeetaAnchor from "@keetanetwork/anchor";
import type { CertificateAttributeNames } from "@keetanetwork/anchor/lib/certificates.js";
import type { KeetaAssetMovementAnchorProvider } from "@keetanetwork/anchor/services/asset-movement/client.js";
import { Errors } from "@keetanetwork/anchor/services/asset-movement/common.js";
import { debugPrintableObject as DPO, MetadataRoot, promptUser } from "../helper.js";
import * as util from "util";

const Account = KeetaAnchor.KeetaNet.lib.Account;

const DEBUG = false;
const logger = DEBUG ? { logger: console } : {};
const network = "test";

const KEETA_USD_ASSET = Account.fromPublicKeyString('keeta_any4zllibya6fum3lsoimxmnmeo57nklxlh4c6d6xosfacarfaa3knkiprkmm');
const US_BANK_SOURCE = { type: 'bank-account', account: { type: 'us' }} as const;

/** Keeta Test Network KYC Root CA — used to verify on-chain KYC certificate chains */
const KYC_ROOT_CA_PEM = `-----BEGIN CERTIFICATE-----
MIIBiDCCAS2gAwIBAgIGAZhi7awAMAsGCWCGSAFlAwQDCjApMScwJQYDVQQDEx5L
ZWV0YSBUZXN0IE5ldHdvcmsgS1lDIFJvb3QgQ0EwHhcNMjUwODAxMDAwMDAwWhcN
MjgwODAxMDAwMDAwWjApMScwJQYDVQQDEx5LZWV0YSBUZXN0IE5ldHdvcmsgS1lD
IFJvb3QgQ0EwNjAQBgcqhkjOPQIBBgUrgQQACgMiAAKK1O9NiYvu2sBYNRPfjOpp
sNSMZ1lOVn+psFdk3Ugq2qNjMGEwDwYDVR0TAQH/BAUwAwEB/zAOBgNVHQ8BAf8E
BAMCAMYwHwYDVR0jBBgwFoAUap82oKFjJ2jhIj2CGABULiX4h3owHQYDVR0OBBYE
FGqfNqChYydo4SI9ghgAVC4l+Id6MAsGCWCGSAFlAwQDCgNIADBFAiEAqnl85S6v
bw8HLO+YXhnwqq6GmnY+7tCcnwYtoyDzYTMCIEw7ALqHJp0kO9AExm5sSoC7rPOd
GlX42GsZQW3AJ7Jc
-----END CERTIFICATE-----`;

const KYC_ROOT_CA = new KeetaAnchor.lib.Certificates.Certificate(KYC_ROOT_CA_PEM);

type BaseCertificate = InstanceType<typeof KeetaAnchor.KeetaNet.lib.Utils.Certificate.Certificate>;
type AnchorCertificate = InstanceType<typeof KeetaAnchor.lib.Certificates.Certificate>;
type SharableCertificateAttributes = InstanceType<typeof KeetaAnchor.lib.Certificates.SharableCertificateAttributes>;

/**
 * Map the attribute names requested by the provider to the typed names expected by
 * `SharableCertificateAttributes.fromCertificate`.
 *
 * `neededAttributes` lists attributes the provider can accept; not all are required.
 * Attributes absent on the certificate are skipped by `fromCertificate`.
 */
function resolveAttributeNames(neededAttributes: string[] | undefined): CertificateAttributeNames[] | undefined {
	if (!neededAttributes || neededAttributes.length === 0) {
		return(undefined);
	}

	return(neededAttributes.map(function(name): CertificateAttributeNames {
		KeetaAnchor.lib.Certificates.SharableCertificateAttributes.assertCertificateAttributeName(name);
		return(name);
	}));
}

/**
 * Select an on-chain KYC leaf certificate whose chain terminates at {@link KYC_ROOT_CA}.
 *
 * When the provider supplies `acceptedIssuers`, the leaf must chain to the known
 * test-network root. Opening with `{ subjectKey, store: { root, intermediate } }`
 * verifies the chain; `subjectKey` alone is used only when no issuer filter applies.
 */
async function selectOnChainKYCCertificate(
	userClient: InstanceType<typeof KeetaAnchor.KeetaNet.UserClient>,
	account: InstanceType<typeof Account>,
	requireTrustedChain: boolean
): Promise<{ certificate: AnchorCertificate; intermediates: Set<BaseCertificate> | undefined }> {
	const records = await userClient.client.getAllCertificates(account);
	if (records.length === 0) {
		throw(new Error('No on-chain KYC certificates found for this account'));
	}

	const trustedRoots = requireTrustedChain ? new Set<BaseCertificate>([ KYC_ROOT_CA ]) : new Set<BaseCertificate>();
	const rejections: string[] = [];

	for (const record of records) {
		const intermediateSet = record.intermediates
			? new Set(record.intermediates.getCertificates())
			: undefined;

		try {
			if (trustedRoots.size > 0) {
				const cert = new KeetaAnchor.lib.Certificates.Certificate(record.certificate.toPEM(), {
					subjectKey: account,
					store: {
						root: trustedRoots,
						intermediate: intermediateSet ?? new Set()
					}
				});

				if (!cert.checkValid() || !cert.trusted) {
					rejections.push(`chain not trusted (issuer DN: ${JSON.stringify(cert.issuerDN)})`);
					continue;
				}

				return({ certificate: cert, intermediates: intermediateSet });
			}

			const cert = new KeetaAnchor.lib.Certificates.Certificate(record.certificate.toPEM(), {
				subjectKey: account
			});

			if (!cert.checkValid()) {
				rejections.push('certificate not valid at current time');
				continue;
			}

			return({ certificate: cert, intermediates: intermediateSet });
		} catch (error) {
			rejections.push(error instanceof Error ? error.message : String(error));
		}
	}

	throw(new Error([
		requireTrustedChain
			? 'No on-chain KYC certificate chains to KYC_ROOT_CA'
			: 'No valid on-chain KYC certificate found',
		requireTrustedChain ? 'Ensure kyc-client.ts attached the certificate with intermediates.' : undefined,
		...rejections.map(function(reason) { return(`  - ${reason}`); })
	].filter(function(line): line is string { return(line !== undefined); }).join('\n')));
}

/**
 * Load the on-chain KYC certificate and build a sharable attributes container.
 *
 * Selects a leaf whose chain terminates at {@link KYC_ROOT_CA}, packages the
 * requested attributes, and grants access to each principal from the KYC share
 * instructions so the container can be posted to the provider.
 */
async function buildSharableKYCAttributes(
	userClient: InstanceType<typeof KeetaAnchor.KeetaNet.UserClient>,
	account: InstanceType<typeof Account>,
	kycShareNeeded: InstanceType<typeof Errors.KYCShareNeeded>
): Promise<SharableCertificateAttributes> {
	const { certificate: selectedCertificate, intermediates } = await selectOnChainKYCCertificate(
		userClient,
		account,
		kycShareNeeded.acceptedIssuers.length > 0
	);

	const attributeNames = resolveAttributeNames(kycShareNeeded.neededAttributes);
	const sharable = await KeetaAnchor.lib.Certificates.SharableCertificateAttributes.fromCertificate(
		selectedCertificate,
		intermediates,
		attributeNames
	);

	for (const principal of kycShareNeeded.shareWithPrincipals) {
		await sharable.grantAccess(principal);
	}

	return(sharable);
}

/**
 * Publish on-chain blocks for provider onboarding steps (add certificate, grant permission).
 */
async function executeUserActionNeeded(
	userClient: InstanceType<typeof KeetaAnchor.KeetaNet.UserClient>,
	userActionNeeded: InstanceType<typeof Errors.UserActionNeeded>
): Promise<void> {
	const builder = userClient.initBuilder();
	Errors.UserActionNeeded.addOperationsToBuilder(userActionNeeded.actionsNeeded, builder);
	await userClient.publishBuilder(builder);

	console.log('Onboarding steps completed.\n');
}

/**
 * Request a persistent forwarding address, running any onboarding steps the provider
 * returns via {@link Errors.UserActionNeeded} before retrying.
 */
async function createPersistentForwardingAddressWithOnboarding(
	provider: KeetaAssetMovementAnchorProvider,
	userClient: InstanceType<typeof KeetaAnchor.KeetaNet.UserClient>,
	request: Parameters<KeetaAssetMovementAnchorProvider['createPersistentForwardingAddress']>[0],
	promptBeforeOnboarding: boolean
): Promise<Awaited<ReturnType<KeetaAssetMovementAnchorProvider['createPersistentForwardingAddress']>>> {
	for (;;) {
		try {
			return(await provider.createPersistentForwardingAddress(request));
		} catch (error) {
			if (!Errors.UserActionNeeded.isInstance(error)) {
				throw(error);
			}

			console.log('Onboarding steps required:');
			console.log(util.inspect(DPO(error.actionsNeeded), { depth: 6, colors: true }));

			if (promptBeforeOnboarding) {
				const proceed = await promptUser('\nComplete onboarding steps and retry? (y/n): ');
				if (proceed.trim().toLowerCase() !== 'y') {
					throw(error);
				}
			}

			await executeUserActionNeeded(userClient, error);
		}
	}
}

async function main() {
	console.log("Keeta KYC Example: Request USD Bank Deposit Address");
	console.log("====================================================\n");

	const seed = await promptUser('Enter your Keeta SEED with KYC completed: ');
	if (!seed.trim()) {
		throw(new Error('Invalid seed'));
	}
	const account = Account.fromSeed(seed.trim(), 0);

	console.log(`Keeta Account: ${account.publicKeyString.get()}\n`);

	await using userClient = KeetaAnchor.KeetaNet.UserClient.fromNetwork(network, account);

	const assetMovementClient = new KeetaAnchor.AssetMovement.Client(userClient, {
		root: Account.fromPublicKeyString(MetadataRoot),
		...logger
	});

	const keetaDestination = {
		type: 'chain',
		chain: { type: 'keeta', networkId: userClient.network }
	} as const;

	const assetPair = { from: 'USD' as const, to: KEETA_USD_ASSET };

	// Get the providers for the transfer to trigger KYC share
	const providers = await assetMovementClient.getProvidersForTransfer({
		asset: assetPair,
		from: US_BANK_SOURCE,
		to: keetaDestination
	});

	if (!providers || providers.length === 0) {
		throw(new Error('No asset movement providers found for USD bank deposit to Keeta'));
	}

	const provider = providers[0];
	if (!provider) {
		throw(new Error('Provider is undefined'));
	}

	console.log(`Using provider: ${String(provider.providerID)}`);

	const persistentAddressRequest = {
		account,
		asset: assetPair,
		sourceLocation: US_BANK_SOURCE,
		destinationLocation: keetaDestination,
		destinationAddress: account.publicKeyString.get()
	} as const;

	try {
		const persistentAddress = await createPersistentForwardingAddressWithOnboarding(
			provider,
			userClient,
			persistentAddressRequest,
			true
		);

		console.log('Persistent address created (KYC share was not required):');
		console.log(util.inspect(DPO(persistentAddress), { depth: 6, colors: true }));
	} catch (error) {
		if (!Errors.KYCShareNeeded.isInstance(error)) {
			throw(error);
		}

		console.log('KYC Share Instructions:');
		console.log(util.inspect(DPO({
			message: error.message,
			neededAttributes: error.neededAttributes,
			shareWithPrincipals: error.shareWithPrincipals.map(function(principal) {
				return(principal.publicKeyString.get());
			}),
			acceptedIssuers: error.acceptedIssuers,
			tosFlow: error.tosFlow
		}), { depth: 6, colors: true }));

		const proceed = await promptUser('\nShare KYC attributes and retry? (y/n): ');
		if (proceed.trim().toLowerCase() !== 'y') {
			console.log('Exiting without sharing KYC attributes.');
			return;
		}

		const sharable = await buildSharableKYCAttributes(userClient, account, error);

		const sharedAttributeNames = await sharable.getAttributeNames();
		console.log(`Sharing ${sharedAttributeNames.length} KYC attributes: ${sharedAttributeNames.join(', ')}\n`);

		// Optional: some providers include `tosFlow` when TOS must be accepted out-of-band
		// before KYC sharing succeeds. Extend here to pass `tosAgreement: { id }` once the
		// flow returns an ID.
		if (error.tosFlow?.type === 'url-flow') {
			console.log(`\nAccept Terms of Service:\n  ${error.tosFlow.url}\n`);
			await promptUser('Press Enter after accepting TOS: ');
		}

		await provider.shareKYCAttributes({
			account,
			attributes: sharable
		});
		console.log('KYC attributes shared.\n');

		// Create a persistent forwarding address to confirm KYC share was successful
		const persistentAddress = await createPersistentForwardingAddressWithOnboarding(
			provider,
			userClient,
			persistentAddressRequest,
			true
		);

		console.log('Persistent address created:');
		console.log(util.inspect(DPO(persistentAddress), { depth: 6, colors: true }));
	}
}

main().then(function() {
	process.exit(0);
}, function(err: unknown) {
	console.error(err);
	process.exit(1);
});
