package network.keeta.examples;

import java.nio.charset.StandardCharsets;

/**
 * Example demonstrating multisig account creation and usage with Keetanet
 * 
 * This example mirrors the TypeScript accounts-multisig-signer.ts example,
 * showing how to:
 * 1. Generate a random seed and create accounts
 * 2. Create a multisig identifier
 * 3. Generate signer accounts
 * 4. Sign and verify messages with cryptographic accounts
 * 
 * Note: This example covers account, block, and signing operations available
 * through the Rust keetanetwork crates. Transmitting the resulting blocks to
 * the network is not yet implemented; that would require a Java HTTP client
 * to interact with a Keetanet node.
 */
public class AccountsMultisigSigner {
    
    public static void main(String[] args) {
        try {
            System.out.println("=== Keetanet Multisig Example (Java + Rust JNI) ===\n");
            
            // Step 1: Generate a random seed
            System.out.println("Step 1: Generating random seed...");
            String seed = Account.generateRandomSeed();
            System.out.println("Seed: " + seed);
            System.out.println();
            
            // Step 2: Create user account from seed (index 0, ED25519 algorithm)
            System.out.println("Step 2: Creating user account from seed...");
            try (Account userAccount = Account.fromSeed(seed, 0, Account.ED25519)) {
                System.out.println("User Account: " + userAccount.getPublicKey());
                System.out.println("Account Type: " + getAccountTypeName(userAccount.getAccountType()));
                System.out.println();
                
                // Step 3: Generate 3 signer accounts (indices 1, 2, 3)
                System.out.println("Step 3: Creating signer accounts...");
                try (Account signer1 = Account.fromSeed(seed, 1, Account.ED25519);
                     Account signer2 = Account.fromSeed(seed, 2, Account.ED25519);
                     Account signer3 = Account.fromSeed(seed, 3, Account.ED25519)) {
                    
                    System.out.println("Signer 1: " + signer1.getPublicKey());
                    System.out.println("Signer 2: " + signer2.getPublicKey());
                    System.out.println("Signer 3: " + signer3.getPublicKey());
                    System.out.println();
                    
                    // Step 4: Generate a multisig identifier
                    System.out.println("Step 4: Generating multisig identifier...");
                    try (Account multisigIdentifier = userAccount.generateMultisigIdentifier(0)) {
                        System.out.println("Multisig Identifier: " + multisigIdentifier.getPublicKey());
                        System.out.println("Account Type: " + getAccountTypeName(multisigIdentifier.getAccountType()));
                        System.out.println();
                        
                        // Step 5: Demonstrate cryptographic signing
                        System.out.println("Step 5: Demonstrating cryptographic operations...");
                        demonstrateSigning(userAccount);
                        System.out.println();
                        
                        // Step 6: Build blocks matching the TypeScript example structure
                        System.out.println("Step 6: Building and signing blocks...");
                        demonstrateBlockBuilding(userAccount, multisigIdentifier, signer1, signer2, signer3);
                        System.out.println();

                        // Step 7: Summary
                        System.out.println("Step 7: Summary");
                        System.out.println("================");
                        System.out.println("User Account:        " + userAccount.getPublicKey());
                        System.out.println("Multisig Identifier: " + multisigIdentifier.getPublicKey());
                        System.out.println("Signer 1:            " + signer1.getPublicKey());
                        System.out.println("Signer 2:            " + signer2.getPublicKey());
                        System.out.println("Signer 3:            " + signer3.getPublicKey());
                        System.out.println();

                        System.out.println("✓ Successfully demonstrated:");
                        System.out.println("  - Account creation and key derivation");
                        System.out.println("  - Multisig identifier generation");
                        System.out.println("  - Message signing and verification");
                        System.out.println("  - identifierBlock: CREATE_IDENTIFIER + MODIFY_PERMISSIONS, signed by userAccount");
                        System.out.println("  - Token identifier (customToken) derived from userAccount");
                        System.out.println("  - multisigExampleBlock: SET_INFO on customToken, signed by multisig (signer1+signer2, 2-of-3)");
                        System.out.println("  - Full ASN.1/DER block serialization");
                        System.out.println();
                        System.out.println("Next steps:");
                        System.out.println("  1. Add HTTP client code to transmit blocks to a Keetanet node");
                        System.out.println("  2. Request tokens from a faucet");
                        System.out.println("  3. Build complete applications!");
                    }
                }
            }
            
            System.out.println("\nExample completed successfully!");
            
        } catch (Exception e) {
            System.err.println("Error: " + e.getMessage());
            e.printStackTrace();
            System.exit(1);
        }
    }
    
    private static void demonstrateSigning(Account account) {
        // Create a test message
        String messageStr = "Hello, Keeta Network from Java!";
        byte[] message = messageStr.getBytes(StandardCharsets.UTF_8);

        System.out.println("Message: " + messageStr);

        // Sign the message
        byte[] signature = account.sign(message);
        System.out.println("Signature length: " + signature.length + " bytes");
        System.out.println("Signature (hex): " + bytesToHex(signature));

        // Verify the signature
        boolean isValid = account.verify(message, signature);
        System.out.println("Signature verification: " + (isValid ? "VALID ✓" : "INVALID ✗"));
        if (!isValid) {
            throw new RuntimeException("Signature verification failed");
        }

        // Try verifying with wrong message
        byte[] wrongMessage = "Wrong message".getBytes(StandardCharsets.UTF_8);
        boolean isInvalid = account.verify(wrongMessage, signature);
        System.out.println("Wrong message verification: " + (isInvalid ? "VALID (unexpected!)" : "INVALID (expected) ✓"));
        if (isInvalid) {
            throw new RuntimeException("Signature unexpectedly verified against the wrong message");
        }
    }
    
    // Network IDs matching the TypeScript SDK config (node/src/config/index.ts).
    // These are protocol-level configuration values, not Rust library constants.
    static final long NETWORK_TEST = 0x54455354L; // 'test' network

    /**
     * Mirrors the TypeScript accounts-multisig-signer.ts example:
     *
     *  1. identifierBlock  — account=userAccount, previous=NO_PREVIOUS
     *                        ops: CREATE_IDENTIFIER(multisig) + MODIFY_PERMISSIONS(grant ADMIN to multisig)
     *                        signed by: userAccount
     *
     *  2. customToken      — TOKEN identifier derived from userAccount (mirrors userClient.generateIdentifier(TOKEN))
     *
     *  3. multisigExampleBlock — account=customToken, previous=NO_PREVIOUS
     *                           signer=[multisig, [signer1, signer2]]  (only 2 of 3 — the quorum)
     *                           ops: SET_INFO(name, description, metadata, defaultPermission=ACCESS)
     *                           signed by: signer1, signer2
     */
    private static void demonstrateBlockBuilding(Account userAccount, Account multisig,
                                                  Account signer1, Account signer2, Account signer3) {
        // --- Block 1: identifierBlock ---
        // Opening block for userAccount.
        // Creates the multisig identifier and grants it ADMIN on userAccount.
        // previous=NO_PREVIOUS so the identifier is derived from the account opening hash,
        // which matches userAccount.generateMultisigIdentifier(0).
        System.out.println("=== Block 1: identifierBlock (userAccount opening block) ===");

        byte[] identifierBlockHash;
        try (Block.Builder builder = new Block.Builder(NETWORK_TEST, userAccount, null)) {
            // op[0]: CREATE_IDENTIFIER for the multisig (quorum=2, signers=signer1,signer2,signer3)
            byte[] createIdOp = multisig.createMultisigOperation(signer1, signer2, signer3, 2);
            builder.addOperation(createIdOp);

            // op[1]: MODIFY_PERMISSIONS — grant ADMIN to multisig on userAccount
            // ACCESS is always implicitly included with any permission grant
            byte[] modifyOp = Account.createModifyPermissionsOperation(multisig, Permissions.ADMIN | Permissions.ACCESS, 2 /* SET */);
            builder.addOperation(modifyOp);

            try (Block.UnsignedBlock unsigned = builder.seal()) {
                identifierBlockHash = unsigned.hash();
                System.out.println("  identifierBlock hash: " + bytesToHexFull(identifierBlockHash));

                try (Block.SignedBlock signed = unsigned.sign(userAccount)) {
                    byte[] blockBytes = signed.toBytes();
                    System.out.println("  Serialized size: " + blockBytes.length + " bytes");
                    System.out.println("  Block bytes (hex): " + bytesToHexFull(blockBytes));
                }
            }
        }

        System.out.println();

        // --- customToken ---
        // Mirrors userClient.generateIdentifier(TOKEN): a TOKEN identifier derived from
        // userAccount with NO_PREVIOUS (operation index 0).
        try (Account customToken = userAccount.generateTokenIdentifier(0)) {
            System.out.println("  Custom Token: " + customToken.getPublicKey());
            System.out.println();

            // --- Block 2: multisigExampleBlock ---
            // Sets token info on customToken.
            // Signed by the multisig identifier using only signer1 + signer2 (the quorum of 2).
            System.out.println("=== Block 2: multisigExampleBlock (customToken, signed by multisig 2-of-3) ===");

            String basicMetadata = java.util.Base64.getEncoder().encodeToString(
                "{\"decimalPlaces\":6}".getBytes(StandardCharsets.UTF_8));

            try (Block.Builder builder = new Block.Builder(NETWORK_TEST, customToken, null)) {
                // signer = [multisig, [signer1, signer2]] — only 2 of 3 signers (the quorum)
                builder.signer(multisig, new Account[]{signer1, signer2});

                // SET_INFO: name, description, metadata, defaultPermission=ACCESS
                byte[] setInfoOp = Account.createSetInfoOperation(
                    "TKNM",
                    "Test Multisig Token Example",
                    basicMetadata,
                    Permissions.ACCESS
                );
                builder.addOperation(setInfoOp);

                try (Block.UnsignedBlock unsigned = builder.seal()) {
                    byte[] hash = unsigned.hash();
                    System.out.println("  multisigExampleBlock hash: " + bytesToHexFull(hash));

                    var requiredSigners = unsigned.getRequiredSigners();
                    System.out.println("  Required signers: " + requiredSigners.size());
                    for (int i = 0; i < requiredSigners.size(); i++) {
                        System.out.println("    Signer " + (i + 1) + ": " + bytesToHex(requiredSigners.get(i)));
                    }

                    // Sign with signer1 and signer2 only (quorum=2)
                    try (Block.SignedBlock signed = unsigned.signMultisig(new Account[]{signer1, signer2})) {
                        System.out.println("  Signed block hash: " + signed.getHashHex());
                        byte[] blockBytes = signed.toBytes();
                        System.out.println("  Serialized size: " + blockBytes.length + " bytes");
                        System.out.println("  Block bytes (hex): " + bytesToHexFull(blockBytes));
                    }
                }
            }
        }

        System.out.println();
        System.out.println("✓ Block building demonstration complete!");
    }

    private static String getAccountTypeName(int type) {
        if (type == Account.ECDSA_SECP256K1) return "ECDSA_SECP256K1";
        if (type == Account.ED25519)         return "ED25519";
        if (type == Account.NETWORK)         return "NETWORK";
        if (type == Account.TOKEN)           return "TOKEN";
        if (type == Account.STORAGE)         return "STORAGE";
        if (type == Account.ECDSA_SECP256R1) return "ECDSA_SECP256R1";
        if (type == Account.MULTISIG)        return "MULTISIG";
        return "UNKNOWN";
    }

    private static String bytesToHex(byte[] bytes) {
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < Math.min(bytes.length, 32); i++) {
            sb.append(String.format("%02x", bytes[i]));
        }
        if (bytes.length > 32) {
            sb.append("...");
        }
        return sb.toString();
    }

    private static String bytesToHexFull(byte[] bytes) {
        StringBuilder sb = new StringBuilder();
        for (byte b : bytes) {
            sb.append(String.format("%02X", b));
        }
        return sb.toString();
    }
}
