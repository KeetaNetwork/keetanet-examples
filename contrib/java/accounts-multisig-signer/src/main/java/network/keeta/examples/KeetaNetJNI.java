package network.keeta.examples;

/**
 * JNI Bridge to Rust Keetanet libraries
 * Provides native methods for account management, multisig operations, and cryptographic signing
 */
public class KeetaNetJNI {
    static {
        System.loadLibrary("keetanet_jni_multisig");
    }

    // Account management
    public static native long[] getAccountTypeConstants();
    public static native long[] getPermissionConstants();
    public static native String generateRandomSeed();
    public static native long accountFromSeed(String seedHex, int index, int keyType);
    public static native long accountFromPublicKeyString(String publicKeyString);
    public static native String getAccountPublicKey(long accountPtr);
    public static native byte[] getAccountPublicKeyAndType(long accountPtr);
    public static native boolean accountHasPrivateKey(long accountPtr);
    public static native boolean accountIsIdentifier(long accountPtr);
    // blockHash may be null to derive against the account opening hash
    public static native long generateIdentifier(long accountPtr, int identifierType, byte[] blockHash, int operationIndex);
    public static native void freeAccount(long accountPtr);
    public static native int getAccountType(long accountPtr);

    // Cryptographic operations
    public static native byte[] signMessage(long accountPtr, byte[] message);
    public static native int verifySignature(long accountPtr, byte[] message, byte[] signature);
    
    // Block operations — each returns an Operation handle (see Operation.java)
    public static native long createMultisigOperation(
        long multisigPtr,
        long signer1Ptr,
        long signer2Ptr,
        long signer3Ptr,
        int quorum
    );
    
    public static native long createModifyPermissionsOperation(
        long principalPtr,
        long permissionsBits,
        int adjustMethod
    );

    public static native long createSetInfoOperation(
        String name,
        String description,
        String metadata,
        long defaultPermissionBits
    );

    public static native void freeOperation(long operationPtr);
    
    // Block builder methods
    public static native long createBlockBuilder();
    public static native long blockBuilderSetVersion(long builderPtr, int version);
    public static native long blockBuilderSetNetwork(long builderPtr, long network);
    public static native long blockBuilderSetAccount(long builderPtr, long accountPtr);
    public static native long blockBuilderSetSigner(long builderPtr, long signerPtr);
    public static native long blockBuilderSetMultisigSigner(long builderPtr, long multisigPtr, long[] signerPtrs);
    public static native long blockBuilderSetPrevious(long builderPtr, byte[] previousHash);
    public static native long blockBuilderSetNoPrevious(long builderPtr);
    public static native long blockBuilderAddOperation(long builderPtr, long operationPtr);
    public static native long blockBuilderBuild(long builderPtr);
    
    // Unsigned block methods
    public static native byte[] unsignedBlockGetHash(long unsignedPtr);
    public static native String unsignedBlockGetHashString(long unsignedPtr);
    public static native byte[] unsignedBlockGetSigners(long unsignedPtr);
    // Signs with the private keys held by the block's required signer
    // accounts and seals the block. Consumes the unsigned block handle.
    public static native long unsignedBlockSign(long unsignedPtr);
    
    // Signed block methods
    public static native byte[] signedBlockGetHash(long signedPtr);
    public static native String signedBlockGetHashString(long signedPtr);
    public static native byte[] signedBlockToBytes(long signedPtr);

    // User client methods (network transmission)
    public static native long userClientFromNetwork(String networkName, long signerPtr);
    public static native long userClientGetBaseToken(long clientPtr);
    public static native String userClientGetBalance(long clientPtr, long accountPtr, long tokenPtr);
    public static native byte[] userClientHead(long clientPtr);
    public static native byte[] userClientHeadForAccount(long clientPtr, long accountPtr);
    public static native boolean userClientTransmit(long clientPtr, long[] signedBlockPtrs);
    public static native long userClientGenerateIdentifier(long clientPtr, int keyType);
    public static native boolean userClientUpdatePermissions(
        long clientPtr,
        long principalPtr,
        long permissionsBits,
        int adjustMethod,
        long targetPtr
    );
    public static native void freeUserClient(long clientPtr);
    
    // Memory management
    public static native void freeBlockBuilder(long builderPtr);
    public static native void freeUnsignedBlock(long unsignedPtr);
    public static native void freeSignedBlock(long signedPtr);
}
