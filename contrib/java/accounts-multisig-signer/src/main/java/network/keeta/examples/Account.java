package network.keeta.examples;

/**
 * Account class, which is used to represent a key pair or an identifier
 * account (which have no private key) such as tokens.
 *
 * This mirrors the public API of the reference TypeScript implementation
 * ({@code node/src/lib/account.ts}) where the underlying Rust crates support
 * it. Features not exposed here include {@code seedFromPassphrase},
 * {@code generateNetworkAddress}/{@code generateBaseAddresses},
 * {@code fromASN1}, raw private/public key constructors, encryption, and
 * {@code ExternalKeyPair}.
 *
 * The native account handle is owned by this object and must be released
 * with {@link #close()} (use try-with-resources).
 */
public class Account implements AutoCloseable {
    /**
     * Account key algorithms specify how the key should be used for
     * validation, signing, and encoding. Values match the reference
     * {@code AccountKeyAlgorithm} enum and the Rust {@code KeyPairType} enum.
     */
    public enum AccountKeyAlgorithm {
        ECDSA_SECP256K1(0),
        ED25519(1),
        NETWORK(2),
        TOKEN(3),
        STORAGE(4),
        ECDSA_SECP256R1(6), // NIST P-256
        MULTISIG(7);

        private final int value;

        AccountKeyAlgorithm(int value) {
            this.value = value;
        }

        public int value() {
            return value;
        }

        public static AccountKeyAlgorithm fromValue(int value) {
            for (AccountKeyAlgorithm algorithm : values()) {
                if (algorithm.value == value) {
                    return algorithm;
                }
            }
            throw new IllegalArgumentException("Unknown account key algorithm: " + value);
        }
    }

    static {
        // Sanity-check the Java enum values against the Rust KeyPairType enum.
        // Order: [ECDSA_SECP256K1, ED25519, NETWORK, TOKEN, STORAGE, ECDSA_SECP256R1, MULTISIG]
        long[] v = KeetaNetJNI.getAccountTypeConstants();
        if (v == null || v.length < 7) {
            throw new ExceptionInInitializerError(
                "Native getAccountTypeConstants() returned "
                    + (v == null ? "null" : ("length " + v.length))
                    + ", expected at least 7 entries");
        }
        AccountKeyAlgorithm[] order = {
            AccountKeyAlgorithm.ECDSA_SECP256K1,
            AccountKeyAlgorithm.ED25519,
            AccountKeyAlgorithm.NETWORK,
            AccountKeyAlgorithm.TOKEN,
            AccountKeyAlgorithm.STORAGE,
            AccountKeyAlgorithm.ECDSA_SECP256R1,
            AccountKeyAlgorithm.MULTISIG,
        };
        for (int i = 0; i < order.length; i++) {
            if (order[i].value() != v[i]) {
                throw new ExceptionInInitializerError(
                    "AccountKeyAlgorithm." + order[i] + " = " + order[i].value()
                        + " does not match native KeyPairType value " + v[i]);
            }
        }
    }

    private long nativePtr;
    private boolean freed = false;

    private Account(long ptr) {
        this.nativePtr = ptr;
    }

    static Account fromNativePtr(long ptr) {
        if (ptr == 0) {
            throw new RuntimeException("Native account handle is null");
        }
        return new Account(ptr);
    }

    /* ------------------------------------------------------------------ *
     * Static constructors                                                 *
     * ------------------------------------------------------------------ */

    /**
     * Construct an account from a public key string.  The public key
     * string encodes the type and public key data.
     */
    public static Account fromPublicKeyString(String publicKeyString) {
        if (publicKeyString == null) {
            throw new IllegalArgumentException("publicKeyString must not be null");
        }
        long ptr = KeetaNetJNI.accountFromPublicKeyString(publicKeyString);
        if (ptr == 0) {
            throw new RuntimeException("Failed to parse public key string: " + publicKeyString);
        }
        return fromNativePtr(ptr);
    }

    /**
     * Construct an account from a seed and index.
     */
    public static Account fromSeed(String seedHex, int index, AccountKeyAlgorithm keyType) {
        if (index < 0) {
            throw new IllegalArgumentException("index must be >= 0, got " + index);
        }
        long ptr = KeetaNetJNI.accountFromSeed(seedHex, index, keyType.value());
        if (ptr == 0) {
            throw new RuntimeException("Failed to create account from seed");
        }
        return fromNativePtr(ptr);
    }

    /**
     * Construct an account from a seed and index, defaulting to
     * ECDSA_SECP256K1 like the reference implementation.
     */
    public static Account fromSeed(String seedHex, int index) {
        return fromSeed(seedHex, index, AccountKeyAlgorithm.ECDSA_SECP256K1);
    }

    /**
     * Securely generate a new random seed value, returned as a hex string
     * (the reference implementation's {@code asString: true} form).
     */
    public static String generateRandomSeed() {
        String seed = KeetaNetJNI.generateRandomSeed();
        if (seed == null) {
            throw new RuntimeException("Failed to generate random seed");
        }
        return seed;
    }

    /* ------------------------------------------------------------------ *
     * Static helpers                                                      *
     * ------------------------------------------------------------------ */

    /**
     * Convert a public key string to an Account (parses the string).
     */
    public static Account toAccount(String publicKeyString) {
        if (publicKeyString == null) {
            return null;
        }
        return fromPublicKeyString(publicKeyString);
    }

    /**
     * Identity conversion, mirroring the reference {@code toAccount} helper.
     */
    public static Account toAccount(Account account) {
        return account;
    }

    /**
     * Get the public key string for an account (null-safe).
     */
    public static String toPublicKeyString(Account account) {
        if (account == null) {
            return null;
        }
        return account.publicKeyString();
    }

    /**
     * Compare the public keys of two accounts (null-safe; two nulls compare
     * equal, as in the reference implementation).
     */
    public static boolean comparePublicKeys(Account acct1, Account acct2) {
        String key1 = toPublicKeyString(acct1);
        String key2 = toPublicKeyString(acct2);
        if (key1 == null || key2 == null) {
            return key1 == key2;
        }
        return key1.equals(key2);
    }

    /**
     * Determine if a key type is an identifier key type (NETWORK, TOKEN,
     * STORAGE, or MULTISIG).  Identifiers are derived addresses with no
     * key pair of their own.
     */
    public static boolean isIdentifierKeyType(AccountKeyAlgorithm keyType) {
        switch (keyType) {
            case NETWORK:
            case TOKEN:
            case STORAGE:
            case MULTISIG:
                return true;
            default:
                return false;
        }
    }

    /* ------------------------------------------------------------------ *
     * Signing                                                             *
     * ------------------------------------------------------------------ */

    /**
     * Sign some data and generate a detached signature.
     */
    public byte[] sign(byte[] data) {
        byte[] signature = KeetaNetJNI.signMessage(getNativePtr(), data);
        if (signature == null) {
            throw new RuntimeException("Failed to sign data");
        }
        return signature;
    }

    /**
     * Verify a detached signature against some data.
     */
    public boolean verify(byte[] data, byte[] signature) {
        return KeetaNetJNI.verifySignature(getNativePtr(), data, signature) == 1;
    }

    /* ------------------------------------------------------------------ *
     * Accessors                                                           *
     * ------------------------------------------------------------------ */

    /**
     * Get the encoded ({@code keeta_}-prefixed) public key string.
     */
    public String publicKeyString() {
        String publicKey = KeetaNetJNI.getAccountPublicKey(getNativePtr());
        if (publicKey == null) {
            throw new RuntimeException("Failed to get account public key");
        }
        return publicKey;
    }

    /**
     * Get the type of key for this account.
     */
    public AccountKeyAlgorithm keyType() {
        return AccountKeyAlgorithm.fromValue(KeetaNetJNI.getAccountType(getNativePtr()));
    }

    /**
     * Get the raw public key prefixed with its key type byte.
     */
    public byte[] publicKeyAndType() {
        byte[] keyData = KeetaNetJNI.getAccountPublicKeyAndType(getNativePtr());
        if (keyData == null) {
            throw new RuntimeException("Failed to get account public key and type");
        }
        return keyData;
    }

    /**
     * Get {@link #publicKeyAndType()} as a {@code 0x}-prefixed hex string.
     */
    public String publicKeyAndTypeString() {
        String keyData = KeetaNetJNI.getAccountPublicKeyAndTypeString(getNativePtr());
        if (keyData == null) {
            throw new RuntimeException("Failed to get account public key and type string");
        }
        return keyData;
    }

    /**
     * Determine if this account has a private key associated with it.
     */
    public boolean hasPrivateKey() {
        return KeetaNetJNI.accountHasPrivateKey(getNativePtr());
    }

    /* ------------------------------------------------------------------ *
     * Identifiers                                                         *
     * ------------------------------------------------------------------ */

    /**
     * Derive an identifier account (NETWORK/TOKEN/STORAGE/MULTISIG) relative
     * to this account, a block hash, and an operation index.
     *
     * @param type           identifier key algorithm to derive
     * @param blockHash      32-byte previous block hash, or null to derive
     *                       against the account opening hash (an opening block)
     * @param operationIndex index of the CREATE_IDENTIFIER operation within
     *                       the block
     */
    public Account generateIdentifier(AccountKeyAlgorithm type, byte[] blockHash, int operationIndex) {
        if (!isIdentifierKeyType(type)) {
            throw new IllegalArgumentException(type + " is not an identifier key type");
        }
        if (operationIndex < 0) {
            throw new IllegalArgumentException("operationIndex must be >= 0, got " + operationIndex);
        }
        if (blockHash != null && blockHash.length != 32) {
            throw new IllegalArgumentException("blockHash must be 32 bytes, got " + blockHash.length);
        }
        long ptr = KeetaNetJNI.generateIdentifier(getNativePtr(), type.value(), blockHash, operationIndex);
        if (ptr == 0) {
            throw new RuntimeException("Failed to generate " + type + " identifier");
        }
        return new Account(ptr);
    }

    /**
     * Determine if an account is an identifier.
     */
    public boolean isIdentifier() {
        return KeetaNetJNI.accountIsIdentifier(getNativePtr());
    }

    public boolean isAccount() {
        return !isIdentifier();
    }

    public boolean isKeyType(AccountKeyAlgorithm checkKeyType) {
        return keyType() == checkKeyType;
    }

    public boolean isStorage() {
        return isKeyType(AccountKeyAlgorithm.STORAGE);
    }

    public boolean isNetwork() {
        return isKeyType(AccountKeyAlgorithm.NETWORK);
    }

    public boolean isToken() {
        return isKeyType(AccountKeyAlgorithm.TOKEN);
    }

    public boolean isMultisig() {
        return isKeyType(AccountKeyAlgorithm.MULTISIG);
    }

    /* ------------------------------------------------------------------ *
     * Assertions and comparisons                                          *
     * ------------------------------------------------------------------ */

    public Account assertKeyType(AccountKeyAlgorithm keyType) {
        if (!isKeyType(keyType)) {
            throw new IllegalStateException("Operation required " + keyType + " but got " + keyType());
        }
        return this;
    }

    public Account assertAccount() {
        if (isIdentifier()) {
            throw new IllegalStateException("Required Account but got Identifier");
        }
        return this;
    }

    public Account assertIdentifier() {
        if (!isIdentifier()) {
            throw new IllegalStateException("Required Identifier but got Account, " + keyType());
        }
        return this;
    }

    public boolean comparePublicKey(Account account) {
        return comparePublicKeys(this, account);
    }

    /**
     * Mirrors the reference {@code toJSON()}: the public key string.
     */
    @Override
    public String toString() {
        return publicKeyString();
    }

    /* ------------------------------------------------------------------ *
     * Native handle management                                            *
     * ------------------------------------------------------------------ */

    long getNativePtr() {
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
}
