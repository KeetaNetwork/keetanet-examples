#! /usr/bin/env ts-node

/*
 * Description: Example of using the Keeta Anchor Client to programmatically add KYC to a wallet
 * The KYC certificate is required by some anchors (e.g. ACH)
 * Verification is performed in Footprint's Sandbox environment (for testnet)
 *
 * Tip: Use the sandbox Email and Phone numbers for your convenience
 * https://docs.onefootprint.com/articles/guides/sandbox-mode#fixture-contact-info
 */

import * as KeetaAnchor from "@keetanetwork/anchor";
import { getFaucetTokens, promptUser } from "../helper.js";

const Account = KeetaAnchor.KeetaNet.lib.Account;

const DEBUG = false;
const logger = DEBUG ? { logger: console } : {};
const network = "test";

async function main() {
	console.log("Keeta KYC Example: Add KYC Verification to a Wallet");
	console.log("====================================================\n");

	// Generate a random account for this demo
	const seed = Account.generateRandomSeed({ asString: true });
	const userAccount = Account.fromSeed(seed, 0);

	console.log(`Seed: ${seed}`);
	console.log(`Keeta Account: ${userAccount.publicKeyString.get()}\n`);

	// Create UserClient for the Keeta Test Network
	await using userClient = KeetaAnchor.KeetaNet.UserClient.fromNetwork(
		network,
		userAccount
	);

	// Get tokens from the faucet for transaction fees
	const faucetRequest = await getFaucetTokens(userAccount, network);
	if (!faucetRequest) {
		throw(new Error("Failed to get Faucet Tokens"));
	}

	// Create KYC client — the resolver defaults to the network address
	const kycClient = new KeetaAnchor.KYC.Client(userClient, {
		...logger
	});

	// Query which countries the KYC anchors on this network support
	const supportedCountries = await kycClient.getSupportedCountries();
	console.log(
		"Supported Countries:",
		supportedCountries.map((c) => c.code).join(", ")
	);

	if (supportedCountries.length === 0) {
		throw(new Error("No KYC providers found on this network"));
	}

	// Pick 'US' as it's universally supported on the testnet
	const countryCode = "US";
	console.log(`\nUsing country: ${countryCode}\n`);

	// Create a verification request — this discovers KYC providers
	// that serve the requested country and signs the request with
	// the user's account key
	const providers = await kycClient.createVerification({
		countryCodes: [countryCode],
		account: userAccount
	});

	if (providers.length === 0) {
		throw(new Error("No KYC providers returned for the requested country"));
	}

	// List available providers
	console.log(`Found ${providers.length} KYC provider(s):`);
	for (const provider of providers) {
		const providerCA = await provider.ca();
		console.log(`  - ${provider.id}: ${providerCA.subject}`);
	}

	// Pick the Footprint sandbox provider and start the verification flow
	const provider = providers.find((p) => p.id === "Footprint");
	if (!provider) {
		throw(new Error("Footprint KYC provider not found"));
	}
	const verification = await provider.startVerification();

	console.log(`\nVerification started:`);
	console.log(`  Request ID: ${verification.id}`);
	console.log(`  Provider: ${verification.providerID}`);
	console.log(
		// eslint-disable-next-line @typescript-eslint/no-unsafe-member-access
		`  Expected Cost: ${verification.expectedCost.min} - ${verification.expectedCost.max}`
	);

	// The user must complete verification at this URL
	console.log(`
========================================
 COMPLETE YOUR KYC VERIFICATION
========================================
Visit the following URL to complete KYC:

***************************************
IMPORTANT: For onboarding with Bivo in sandbox, use "three" in address line 2 and a unique SSN / Tax ID
Share KYC with Bivo will then be automatically approved.  Otherwise the Share KYC request will timeout.
***************************************

	${verification.webURL}

========================================
`);

	const shouldPoll = await promptUser(
		'Have you completed the verification? Press Enter to start polling for your certificate (or type "skip" to exit): '
	);

	if (shouldPoll.trim().toLowerCase() === "skip") {
		console.log(
			"Exiting. You can poll for your certificate later using the verification ID."
		);
		return;
	}

	// Poll for the KYC certificate
	console.log("Polling for KYC certificate...");

	while (true) {
		const results = await verification.getCertificates();

		if (!results.ok) {
			process.stdout.write(".");
			await KeetaAnchor.KeetaNet.lib.Utils.Helper.asleep(results.retryAfter);
			continue;
		}

		console.log(`\n\nKYC Certificate received!`);
		console.log(`  Number of certificates: ${results.results.length}`);

		const { Block } = KeetaAnchor.KeetaNet.lib;
		const { CertificateBundle } = KeetaAnchor.KeetaNet.lib.Utils.Certificate;

		for (const [i, certGroup] of results.results.entries()) {
			// Re-wrap with the user's account as the subject key so sensitive
			// attributes (PII) can be decrypted
			const cert = new KeetaAnchor.lib.Certificates.Certificate(
				certGroup.certificate.toPEM(),
				{ subjectKey: userAccount }
			);

			console.log(`\n  Certificate ${i + 1}:`);
			console.log(`    Subject: ${cert.subject}`);
			console.log(`    Valid: ${cert.checkValid()}`);

			if (certGroup.intermediates) {
				console.log(
					`    Intermediate certificates: ${certGroup.intermediates.size}`
				);
			}

			if ("fullName" in cert.attributes) {
				const attr = cert.attributes["fullName"];
				const value = attr.sensitive
					? await attr.value.getValue()
					: await cert.getAttributeValue("fullName");
				console.log(`    Full name (decrypted): ${String(value)}`);
			}

			// Attach the certificate to the user's account onchain
			const intermediates = certGroup.intermediates
				? new CertificateBundle([...certGroup.intermediates])
				: null;
			await userClient.modifyCertificate(
				Block.AdjustMethod.ADD,
				cert,
				intermediates
			);
		}

		// Read the certificates back from the chain to confirm attachment
		const onChain = await userClient.client.getAllCertificates(userAccount);
		console.log(`\nOn-chain certificates for this account: ${onChain.length}`);

		console.log(
			"\nKYC verification complete! The KYC certificate is now attached to your account on-chain."
		);
		break;
	}
}

main().then(function() {
	process.exit(0);
}, function(err: unknown) {
	console.error(err);
	process.exit(1);
});
