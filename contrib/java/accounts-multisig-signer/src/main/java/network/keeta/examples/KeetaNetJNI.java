package network.keeta.examples;

import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import java.util.concurrent.ConcurrentHashMap;

/**
 * Java facade over the Rust WASM bridge.
 */
public final class KeetaNetJNI {
    private static final KeetaNetWasmBridge WASM = new KeetaNetWasmBridge();
    private static final Map<String, Long> LIVE_HANDLES = new ConcurrentHashMap<>();

    private KeetaNetJNI() {}

    static String registerNativeHandle(String type, long ptr) {
        if (ptr == 0) {
            throw new IllegalArgumentException("Cannot register null native handle for type " + type);
        }
        String handle = type + "0x" + Long.toUnsignedString(ptr, 16).toUpperCase();
        Long existing = LIVE_HANDLES.putIfAbsent(handle, ptr);
        if (existing != null) {
            throw new IllegalStateException("Native handle collision: " + handle);
        }
        return handle;
    }

    static long requireNativeHandle(String type, String handle) {
        if (handle == null || !handle.startsWith(type + "0x")) {
            throw new IllegalArgumentException("Invalid " + type + " handle format: " + handle);
        }
        Long ptr = LIVE_HANDLES.get(handle);
        if (ptr == null) {
            throw new IllegalStateException("Native handle is not live: " + handle);
        }
        return ptr;
    }

    static long unregisterNativeHandle(String type, String handle) {
        if (handle == null || !handle.startsWith(type + "0x")) {
            throw new IllegalArgumentException("Invalid " + type + " handle format: " + handle);
        }
        Long ptr = LIVE_HANDLES.remove(handle);
        if (ptr == null) {
            throw new IllegalStateException("Native handle already freed or unknown: " + handle);
        }
        return ptr;
    }

    public static int liveHandleCount() {
        return LIVE_HANDLES.size();
    }

    public static void assertNoLiveHandles() {
        if (LIVE_HANDLES.isEmpty()) {
            return;
        }
        List<String> sample = new ArrayList<>();
        for (String handle : LIVE_HANDLES.keySet()) {
            sample.add(handle);
            if (sample.size() >= 8) {
                break;
            }
        }
        throw new IllegalStateException(
            "Native handle leak detected (" + LIVE_HANDLES.size() + " live): " + String.join(", ", sample)
        );
    }

    // Account key-type constants in the same order as prior JNI API.
    public static long[] getAccountTypeConstants() {
        byte[] data = WASM.callBytes("kn_get_account_type_constants");
        return decodeLongArray(data);
    }

    // BaseFlag bit positions.
    public static long[] getPermissionConstants() {
        byte[] data = WASM.callBytes("kn_get_permission_constants");
        return decodeLongArray(data);
    }

    private static long[] decodeLongArray(byte[] data) {
        if (data == null || data.length == 0 || (data.length % Long.BYTES) != 0) {
            throw new RuntimeException("Invalid native long-array payload");
        }
        ByteBuffer bb = ByteBuffer.wrap(data).order(ByteOrder.LITTLE_ENDIAN);
        long[] out = new long[data.length / Long.BYTES];
        for (int i = 0; i < out.length; i++) {
            out[i] = bb.getLong();
        }
        return out;
    }

    public static String generateRandomSeed() {
        String seed = WASM.callString("kn_generate_random_seed");
        if (seed == null) {
            throw new RuntimeException(WASM.lastError());
        }
        return seed;
    }

    public static long accountFromSeed(String seedHex, int index, int keyType) {
        byte[] seed = seedHex.getBytes(StandardCharsets.UTF_8);
        long seedPtr = WASM.allocAndWrite(seed);
        try {
            long handle = WASM.callI64(
                "kn_account_from_seed",
                seedPtr,
                Integer.toUnsignedLong(seed.length),
                Integer.toUnsignedLong(index),
                Integer.toUnsignedLong(keyType)
            );
            return handle;
        } finally {
            WASM.free(seedPtr, seed.length);
        }
    }

    public static long accountFromPublicKeyString(String publicKeyString) {
        byte[] key = publicKeyString.getBytes(StandardCharsets.UTF_8);
        long ptr = WASM.allocAndWrite(key);
        try {
            return WASM.callI64("kn_account_from_public_key_string", ptr, Integer.toUnsignedLong(key.length));
        } finally {
            WASM.free(ptr, key.length);
        }
    }

    public static String getAccountPublicKey(long accountPtr) {
        return WASM.callString("kn_get_account_public_key", accountPtr);
    }

    public static byte[] getAccountPublicKeyAndType(long accountPtr) {
        return WASM.callBytes("kn_get_account_public_key_and_type", accountPtr);
    }

    public static String getAccountPublicKeyAndTypeString(long accountPtr) {
        return WASM.callString("kn_get_account_public_key_and_type_string", accountPtr);
    }

    public static boolean accountHasPrivateKey(long accountPtr) {
        return WASM.callI32("kn_account_has_private_key", accountPtr) != 0;
    }

    public static boolean accountIsIdentifier(long accountPtr) {
        return WASM.callI32("kn_account_is_identifier", accountPtr) != 0;
    }

    public static long generateIdentifier(long accountPtr, int identifierType, byte[] blockHash, int operationIndex) {
        long hashPtr = 0;
        int hashLen = 0;
        if (blockHash != null && blockHash.length > 0) {
            hashLen = blockHash.length;
            hashPtr = WASM.allocAndWrite(blockHash);
        }
        try {
            return WASM.callI64(
                "kn_generate_identifier",
                accountPtr,
                Integer.toUnsignedLong(identifierType),
                hashPtr,
                Integer.toUnsignedLong(hashLen),
                Integer.toUnsignedLong(operationIndex)
            );
        } finally {
            if (hashPtr != 0) {
                WASM.free(hashPtr, hashLen);
            }
        }
    }

    static long networkBaseToken(long networkId) {
        return WASM.callI64("kn_network_base_token", networkId);
    }

    public static void freeAccount(long accountPtr) {
        WASM.callVoid("kn_free_account", accountPtr);
    }

    public static int getAccountType(long accountPtr) {
        return WASM.callI32("kn_get_account_type", accountPtr);
    }

    public static byte[] signMessage(long accountPtr, byte[] message) {
        long ptr = WASM.allocAndWrite(message);
        try {
            return WASM.callBytes("kn_sign_message", accountPtr, ptr, Integer.toUnsignedLong(message.length));
        } finally {
            WASM.free(ptr, message.length);
        }
    }

    public static int verifySignature(long accountPtr, byte[] message, byte[] signature) {
        long messagePtr = WASM.allocAndWrite(message);
        long sigPtr = WASM.allocAndWrite(signature);
        try {
            return WASM.callI32(
                "kn_verify_signature",
                accountPtr,
                messagePtr,
                Integer.toUnsignedLong(message.length),
                sigPtr,
                Integer.toUnsignedLong(signature.length)
            );
        } finally {
            WASM.free(messagePtr, message.length);
            WASM.free(sigPtr, signature.length);
        }
    }

    public static long createIdentifierOperation(long identifierPtr) {
        return WASM.callI64("kn_create_identifier_operation", identifierPtr);
    }

    public static long createMultisigOperation(
        long multisigPtr,
        long signer1Ptr,
        long signer2Ptr,
        long signer3Ptr,
        int quorum
    ) {
        return WASM.callI64(
            "kn_create_multisig_operation",
            multisigPtr,
            signer1Ptr,
            signer2Ptr,
            signer3Ptr,
            Integer.toUnsignedLong(quorum)
        );
    }

    public static long createModifyPermissionsOperation(long principalPtr, long permissionsBits, int adjustMethod) {
        return WASM.callI64(
            "kn_create_modify_permissions_operation",
            principalPtr,
            permissionsBits,
            Integer.toUnsignedLong(adjustMethod)
        );
    }

    public static long createSetInfoOperation(
        String name,
        String description,
        String metadata,
        long defaultPermissionBits
    ) {
        byte[] nameBytes = name.getBytes(StandardCharsets.UTF_8);
        byte[] descBytes = description.getBytes(StandardCharsets.UTF_8);
        byte[] metadataBytes = metadata.getBytes(StandardCharsets.UTF_8);
        long namePtr = WASM.allocAndWrite(nameBytes);
        long descPtr = WASM.allocAndWrite(descBytes);
        long metaPtr = WASM.allocAndWrite(metadataBytes);
        try {
            return WASM.callI64(
                "kn_create_set_info_operation",
                namePtr,
                Integer.toUnsignedLong(nameBytes.length),
                descPtr,
                Integer.toUnsignedLong(descBytes.length),
                metaPtr,
                Integer.toUnsignedLong(metadataBytes.length),
                defaultPermissionBits
            );
        } finally {
            WASM.free(namePtr, nameBytes.length);
            WASM.free(descPtr, descBytes.length);
            WASM.free(metaPtr, metadataBytes.length);
        }
    }

    public static long createSendOperation(long toPtr, long tokenPtr, String amount) {
        byte[] amountBytes = amount.getBytes(StandardCharsets.UTF_8);
        long amountPtr = WASM.allocAndWrite(amountBytes);
        try {
            return WASM.callI64(
                "kn_create_send_operation",
                toPtr,
                tokenPtr,
                amountPtr,
                Integer.toUnsignedLong(amountBytes.length)
            );
        } finally {
            WASM.free(amountPtr, amountBytes.length);
        }
    }

    public static void freeOperation(long operationPtr) {
        WASM.callVoid("kn_free_operation", operationPtr);
    }

    public static long createBlockBuilder() {
        return WASM.callI64("kn_create_block_builder");
    }

    public static long blockBuilderSetVersion(long builderPtr, int version) {
        return WASM.callI64("kn_block_builder_set_version", builderPtr, Integer.toUnsignedLong(version));
    }

    public static long blockBuilderSetNetwork(long builderPtr, long network) {
        return WASM.callI64("kn_block_builder_set_network", builderPtr, network);
    }

    public static long blockBuilderSetPurpose(long builderPtr, int purpose) {
        return WASM.callI64("kn_block_builder_set_purpose", builderPtr, Integer.toUnsignedLong(purpose));
    }

    public static long blockBuilderSetAccount(long builderPtr, long accountPtr) {
        return WASM.callI64("kn_block_builder_set_account", builderPtr, accountPtr);
    }

    public static long blockBuilderSetSigner(long builderPtr, long signerPtr) {
        return WASM.callI64("kn_block_builder_set_signer", builderPtr, signerPtr);
    }

    public static long blockBuilderSetMultisigSigner(long builderPtr, long multisigPtr, long[] signerPtrs) {
        long ptr = WASM.allocAndWriteU64Array(signerPtrs);
        int len = signerPtrs.length * Long.BYTES;
        try {
            return WASM.callI64(
                "kn_block_builder_set_multisig_signer",
                builderPtr,
                multisigPtr,
                ptr,
                Integer.toUnsignedLong(signerPtrs.length)
            );
        } finally {
            if (ptr != 0) {
                WASM.free(ptr, len);
            }
        }
    }

    public static long blockBuilderSetPrevious(long builderPtr, byte[] previousHash) {
        long ptr = WASM.allocAndWrite(previousHash);
        try {
            return WASM.callI64(
                "kn_block_builder_set_previous",
                builderPtr,
                ptr,
                Integer.toUnsignedLong(previousHash.length)
            );
        } finally {
            WASM.free(ptr, previousHash.length);
        }
    }

    public static long blockBuilderSetNoPrevious(long builderPtr) {
        return WASM.callI64("kn_block_builder_set_no_previous", builderPtr);
    }

    public static long blockBuilderAddOperation(long builderPtr, long operationPtr) {
        return WASM.callI64("kn_block_builder_add_operation", builderPtr, operationPtr);
    }

    public static long blockBuilderBuild(long builderPtr) {
        return WASM.callI64("kn_block_builder_build", builderPtr);
    }

    public static byte[] unsignedBlockGetHash(long unsignedPtr) {
        return WASM.callBytes("kn_unsigned_block_get_hash", unsignedPtr);
    }

    public static String unsignedBlockGetHashString(long unsignedPtr) {
        return WASM.callString("kn_unsigned_block_get_hash_string", unsignedPtr);
    }

    public static byte[] unsignedBlockGetSigners(long unsignedPtr) {
        return WASM.callBytes("kn_unsigned_block_get_signers", unsignedPtr);
    }

    public static long unsignedBlockSign(long unsignedPtr) {
        return WASM.callI64("kn_unsigned_block_sign", unsignedPtr);
    }

    public static byte[] signedBlockGetHash(long signedPtr) {
        return WASM.callBytes("kn_signed_block_get_hash", signedPtr);
    }

    public static String signedBlockGetHashString(long signedPtr) {
        return WASM.callString("kn_signed_block_get_hash_string", signedPtr);
    }

    public static String signedBlockGetAccountString(long signedPtr) {
        return WASM.callString("kn_signed_block_get_account_string", signedPtr);
    }

    public static byte[] signedBlockToBytes(long signedPtr) {
        return WASM.callBytes("kn_signed_block_to_bytes", signedPtr);
    }

    public static byte[] createVoteStaple(byte[][] blocks, byte[][] votes) {
        long[][] blockPairs = new long[blocks.length][2];
        long[][] votePairs = new long[votes.length][2];
        int blocksDataLen = 0;
        int votesDataLen = 0;
        for (int i = 0; i < blocks.length; i++) {
            long ptr = WASM.allocAndWrite(blocks[i]);
            blockPairs[i][0] = ptr;
            blockPairs[i][1] = Integer.toUnsignedLong(blocks[i].length);
            blocksDataLen += blocks[i].length;
        }
        for (int i = 0; i < votes.length; i++) {
            long ptr = WASM.allocAndWrite(votes[i]);
            votePairs[i][0] = ptr;
            votePairs[i][1] = Integer.toUnsignedLong(votes[i].length);
            votesDataLen += votes[i].length;
        }

        long blockPairsPtr = WASM.allocAndWritePtrLenArray(blockPairs);
        long votePairsPtr = WASM.allocAndWritePtrLenArray(votePairs);
        try {
            return WASM.callBytes(
                "kn_create_vote_staple",
                blockPairsPtr,
                Integer.toUnsignedLong(blocks.length),
                votePairsPtr,
                Integer.toUnsignedLong(votes.length)
            );
        } finally {
            for (long[] pair : blockPairs) {
                if (pair[0] != 0) {
                    WASM.free(pair[0], (int) pair[1]);
                }
            }
            for (long[] pair : votePairs) {
                if (pair[0] != 0) {
                    WASM.free(pair[0], (int) pair[1]);
                }
            }
            if (blockPairsPtr != 0) {
                WASM.free(blockPairsPtr, blocks.length * Integer.BYTES * 2);
            }
            if (votePairsPtr != 0) {
                WASM.free(votePairsPtr, votes.length * Integer.BYTES * 2);
            }
        }
    }

    public static String voteSelectFee(byte[] voteBytes, long preferredTokenPtr) {
        long votePtr = WASM.allocAndWrite(voteBytes);
        try {
            return WASM.callString(
                "kn_vote_select_fee",
                votePtr,
                Integer.toUnsignedLong(voteBytes.length),
                preferredTokenPtr
            );
        } finally {
            WASM.free(votePtr, voteBytes.length);
        }
    }

    public static void freeBlockBuilder(long builderPtr) {
        WASM.callVoid("kn_free_block_builder", builderPtr);
    }

    public static void freeUnsignedBlock(long unsignedPtr) {
        WASM.callVoid("kn_free_unsigned_block", unsignedPtr);
    }

    public static void freeSignedBlock(long signedPtr) {
        WASM.callVoid("kn_free_signed_block", signedPtr);
    }
}
