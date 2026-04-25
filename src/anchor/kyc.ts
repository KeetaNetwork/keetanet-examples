#! /usr/bin/env ts-node

/*
 * Description: Example of using the Keeta Anchor Client to programmatically add KYC to a wallet
 * The KYC certificate is required by some testnet anchors (e.g. ACH) to simulate live network conditions
 * Verification is performed in Footprint's Sandbox environment
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
    userAccount,
  );

  // Get tokens from the faucet for transaction fees
  const faucetRequest = await getFaucetTokens(userAccount, network);
  if (!faucetRequest) {
    throw new Error("Failed to get Faucet Tokens");
  }

  // Create KYC client — the resolver defaults to the network address
  const kycClient = new KeetaAnchor.KYC.Client(userClient, {
    root: [
      userClient.networkAddress,
      Account.fromPublicKeyString(
        "keeta_aabcchpocp3o5l4hum5dgqhjzh3vfn2dhiqq4hz6y2ngri24ogyi3x6cizw2b3a",
      ),
    ],
    ...logger,
  });

  // Query which countries the KYC anchors on this network support
  const supportedCountries = await kycClient.getSupportedCountries();
  console.log(
    "Supported Countries:",
    supportedCountries.map((c) => c.code).join(", "),
  );

  if (supportedCountries.length === 0) {
    throw new Error("No KYC providers found on this network");
  }

  // Pick 'US' as it's universally supported on the testnet
  const countryCode = "US";
  console.log(`\nUsing country: ${countryCode}\n`);

  // Create a verification request — this discovers KYC providers
  // that serve the requested country and signs the request with
  // the user's account key
  const providers = await kycClient.createVerification({
    countryCodes: [countryCode],
    account: userAccount,
  });

  if (providers.length === 0) {
    throw new Error("No KYC providers returned for the requested country");
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
    throw new Error("Footprint KYC provider not found");
  }
  const verification = await provider.startVerification();

  console.log(`\nVerification started:`);
  console.log(`  Request ID: ${verification.id}`);
  console.log(`  Provider: ${verification.providerID}`);
  console.log(
    `  Expected Cost: ${verification.expectedCost.min} - ${verification.expectedCost.max}`,
  );

  // The user must complete verification at this URL
  console.log(`
========================================
 COMPLETE YOUR KYC VERIFICATION
========================================
Visit the following URL to complete KYC:

  ${verification.webURL}

========================================
`);

  const shouldPoll = await promptUser(
    'Have you completed the verification? Press Enter to start polling for your certificate (or type "skip" to exit): ',
  );

  if (shouldPoll.trim().toLowerCase() === "skip") {
    console.log(
      "Exiting. You can poll for your certificate later using the verification ID.",
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

    for (const [i, certGroup] of results.results.entries()) {
      const cert = certGroup.certificate;
      console.log(`\n  Certificate ${i + 1}:`);
      console.log(`    Subject: ${cert.subject}`);
      console.log(`    Valid: ${cert.checkValid()}`);

      if (certGroup.intermediates) {
        console.log(
          `    Intermediate certificates: ${certGroup.intermediates.size}`,
        );
      }
    }

    console.log(
      "\nKYC verification complete! Your wallet now has a KYC certificate.",
    );
    break;
  }
}

main().then(
  function () {
    process.exit(0);
  },
  function (err: unknown) {
    console.error(err);
    process.exit(1);
  },
);
