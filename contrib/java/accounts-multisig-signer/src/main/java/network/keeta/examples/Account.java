package network.keeta.examples;

/**
 * Wrapper class for Keetanet Account operations
 * Manages native memory and provides high-level account operations
 */
public class Account implements AutoCloseable {
    // Account key-type constants — sourced from the Rust KeyPairType enum via JNI.
    // Order matches getAccountTypeConstants(): [ECDSA_SECP256K1, ED25519, NETWORK, TOKEN, STORAGE, ECDSA_SECP256R1, MULTISIG]
    public static final int ECDSA_SECP256K1;
    public static final int ED25519;
    public static final int NETWORK;
    public static final int TOKEN;
    public static final int STORAGE;
    public static final int ECDSA_SECP256R1;
    public static final int MULTISIG;

    static {
        long[] v = KeetaNetJNI.getAccountTypeConstants();
        ECDSA_SECP256K1 = (int) v[0];
        ED25519         = (int) v[1];
        NETWORK         = (int) v[2];
        TOKEN           = (int) v[3];
        STORAGE         = (int) v[4];
        ECDSA_SECP256R1 = (int) v[5];
        MULTISIG        = (int) v[6];
    }

    private long nativePtr;
    private boolean freed = false;

    private Account(long ptr) {
        this.nativePtr = ptr;
    }

    public static String generateRandomSeed() {
        return KeetaNetJNI.generateRandomSeed();
    }

    public static Account fromSeed(String seedHex, int index, int keyType) {
        long ptr = KeetaNetJNI.accountFromSeed(seedHex, index, keyType);
        if (ptr == 0) {
            throw new RuntimeException("Failed to create account from seed");
        }
        return new Account(ptr);
    }

    public static Account fromSeed(String seedHex, int index) {
        return fromSeed(seedHex, index, ED25519);
    }

    public String getPublicKey() {
        if (freed) {
            throw new IllegalStateException("Account has been freed");
        }
        return KeetaNetJNI.getAccountPublicKey(nativePtr);
    }

    public int getAccountType() {
        if (freed) {
            throw new IllegalStateException("Account has been freed");
        }
        return KeetaNetJNI.getAccountType(nativePtr);
    }

    public Account generateMultisigIdentifier(int operationIndex) {
        if (freed) {
            throw new IllegalStateException("Account has been freed");
        }
        long ptr = KeetaNetJNI.generateMultisigIdentifier(nativePtr, operationIndex);
        if (ptr == 0) {
            throw new RuntimeException("Failed to generate multisig identifier");
        }
        return new Account(ptr);
    }

    public Account generateTokenIdentifier(int operationIndex) {
        if (freed) {
            throw new IllegalStateException("Account has been freed");
        }
        long ptr = KeetaNetJNI.generateTokenIdentifier(nativePtr, operationIndex);
        if (ptr == 0) {
            throw new RuntimeException("Failed to generate token identifier");
        }
        return new Account(ptr);
    }

    public byte[] sign(byte[] message) {
        if (freed) {
            throw new IllegalStateException("Account has been freed");
        }
        return KeetaNetJNI.signMessage(nativePtr, message);
    }

    public boolean verify(byte[] message, byte[] signature) {
        if (freed) {
            throw new IllegalStateException("Account has been freed");
        }
        return KeetaNetJNI.verifySignature(nativePtr, message, signature) == 1;
    }

    public long getNativePtr() {
        if (freed) {
            throw new IllegalStateException("Account has been freed");
        }
        return nativePtr;
    }

    @Override
    public void close() {
        if (!freed && nativePtr != 0) {
            KeetaNetJNI.freeAccount(nativePtr);
            freed = true;
            nativePtr = 0;
        }
    }
    
    // Block operation helpers
    
    /**
     * Create a multisig identifier creation operation
     * @param signer1 First signer account
     * @param signer2 Second signer account
     * @param signer3 Third signer account
     * @param quorum Number of signatures required
     * @return DER-encoded operation bytes
     */
    public byte[] createMultisigOperation(Account signer1, Account signer2, Account signer3, int quorum) {
        if (freed) {
            throw new IllegalStateException("Account has been freed");
        }
        return KeetaNetJNI.createMultisigOperation(
            nativePtr,
            signer1.getNativePtr(),
            signer2.getNativePtr(),
            signer3.getNativePtr(),
            quorum
        );
    }
    
    /**
     * Create a modify permissions operation
     * @param principal Account to grant/revoke permissions to/from
     * @param permissionsBits Permission bits to modify
     * @param adjustMethod 0=ADD, 1=SUBTRACT, 2=SET
     * @return DER-encoded operation bytes
     */
    public static byte[] createModifyPermissionsOperation(Account principal, long permissionsBits, int adjustMethod) {
        return KeetaNetJNI.createModifyPermissionsOperation(
            principal.getNativePtr(),
            permissionsBits,
            adjustMethod
        );
    }

    /**
     * Create a SET_INFO operation
     * @param name Token name
     * @param description Token description
     * @param metadata Base64-encoded metadata string
     * @param accessPermissionBase ACCESS permission bits (0 = no default permission)
     * @return DER-encoded operation bytes
     */
    public static byte[] createSetInfoOperation(String name, String description, String metadata, long accessPermissionBase) {
        return KeetaNetJNI.createSetInfoOperation(name, description, metadata, accessPermissionBase);
    }
}

