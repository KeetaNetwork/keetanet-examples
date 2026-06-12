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
    public static native String getAccountPublicKey(long accountPtr);
    public static native long generateMultisigIdentifier(long accountPtr, int operationIndex);
    public static native long generateTokenIdentifier(long accountPtr, int operationIndex);
    public static native void freeAccount(long accountPtr);
    public static native int getAccountType(long accountPtr);

    // Cryptographic operations
    public static native byte[] signMessage(long accountPtr, byte[] message);
    public static native int verifySignature(long accountPtr, byte[] message, byte[] signature);
    
    // Block operations
    public static native byte[] createMultisigOperation(
        long multisigPtr,
        long signer1Ptr,
        long signer2Ptr,
        long signer3Ptr,
        int quorum
    );
    
    public static native byte[] createModifyPermissionsOperation(
        long principalPtr,
        long permissionsBits,
        int adjustMethod
    );

    public static native byte[] createSetInfoOperation(
        String name,
        String description,
        String metadata,
        long accessPermissionBase
    );
    
    // Block builder methods
    public static native long createBlockBuilder();
    public static native long blockBuilderSetVersion(long builderPtr, int version);
    public static native long blockBuilderSetNetwork(long builderPtr, long network);
    public static native long blockBuilderSetAccount(long builderPtr, long accountPtr);
    public static native long blockBuilderSetSigner(long builderPtr, long signerPtr);
    public static native long blockBuilderSetMultisigSigner(long builderPtr, long multisigPtr, byte[] signerPtrs);
    public static native long blockBuilderSetPrevious(long builderPtr, byte[] previousHash);
    public static native long blockBuilderSetNoPrevious(long builderPtr);
    public static native long blockBuilderAddOperation(long builderPtr, byte[] operationDer);
    public static native long blockBuilderBuild(long builderPtr);
    
    // Unsigned block methods
    public static native byte[] unsignedBlockGetHash(long unsignedPtr);
    public static native byte[] unsignedBlockGetSigners(long unsignedPtr);
    public static native byte[] unsignedBlockSign(long unsignedPtr, long accountPtr, byte[] blockHash);
    public static native long unsignedBlockSeal(long unsignedPtr, byte[] signatures);
    
    // Signed block methods
    public static native byte[] signedBlockGetHash(long signedPtr);
    public static native byte[] signedBlockToBytes(long signedPtr);
    
    // Memory management
    public static native void freeBlockBuilder(long builderPtr);
    public static native void freeUnsignedBlock(long unsignedPtr);
    public static native void freeSignedBlock(long signedPtr);
}
