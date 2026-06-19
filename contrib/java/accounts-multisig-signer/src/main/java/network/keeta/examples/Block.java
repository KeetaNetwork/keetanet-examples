package network.keeta.examples;

import java.util.ArrayList;
import java.util.List;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;

/**
 * Block builder for creating and signing Keetanet blocks
 * 
 * Provides a fluent API for constructing blocks, similar to the TypeScript
 * Block.Builder. Signing uses the private keys held by the accounts placed
 * in the block's signer field, so no signatures cross the JNI boundary.
 */
public class Block {
    public static final int PURPOSE_GENERIC = 0;
    public static final int PURPOSE_FEE = 1;
    
    /**
     * Builder for creating unsigned blocks
     */
    public static class Builder implements AutoCloseable {
        private String builderHandle;
        private boolean closed = false;
        
        /**
         * Create a new block builder
         * 
         * @param network Network ID
         * @param account Account that owns the block
         * @param previousHash Previous block hash (32 bytes), or null for opening blocks
         */
        public Builder(long network, Account account, byte[] previousHash) {
            if (account == null) {
                throw new IllegalArgumentException("account must not be null");
            }
            long builderPtr = KeetaNetJNI.createBlockBuilder();
            if (builderPtr == 0) {
                throw new RuntimeException("Failed to create block builder");
            }
            this.builderHandle = KeetaNetJNI.registerNativeHandle("builder", builderPtr);
            try {
                // Set version to V2 (default)
                builderPtr = KeetaNetJNI.blockBuilderSetVersion(getNativePtr(), 2);
                updateHandle(builderPtr);
                if (builderPtr == 0) {
                    throw new RuntimeException("Failed to set block version");
                }

                // Set network
                builderPtr = KeetaNetJNI.blockBuilderSetNetwork(getNativePtr(), network);
                updateHandle(builderPtr);
                if (builderPtr == 0) {
                    throw new RuntimeException("Failed to set network");
                }

                // Set account
                builderPtr = KeetaNetJNI.blockBuilderSetAccount(getNativePtr(), account.getNativePtr());
                updateHandle(builderPtr);
                if (builderPtr == 0) {
                    throw new RuntimeException("Failed to set account");
                }

                // Set previous hash, or mark as the account opening block
                if (previousHash != null) {
                    if (previousHash.length != 32) {
                        throw new IllegalArgumentException("Previous hash must be 32 bytes");
                    }
                    builderPtr = KeetaNetJNI.blockBuilderSetPrevious(getNativePtr(), previousHash);
                } else {
                    builderPtr = KeetaNetJNI.blockBuilderSetNoPrevious(getNativePtr());
                }
                updateHandle(builderPtr);

                if (builderPtr == 0) {
                    throw new RuntimeException("Failed to set previous hash");
                }
            } catch (RuntimeException e) {
                if (this.builderHandle != null) {
                    KeetaNetJNI.freeBlockBuilder(getNativePtr());
                    KeetaNetJNI.unregisterNativeHandle("builder", this.builderHandle);
                    this.builderHandle = null;
                }
                closed = true;
                throw e;
            }
        }
        
        /**
         * Set a single-account signer for this block.
         * Defaults to the block account if never called.
         * 
         * @param signer Signing account (must hold a private key)
         * @return this builder
         */
        public Builder signer(Account signer) {
            if (closed) {
                throw new IllegalStateException("Builder has been closed");
            }

            long builderPtr = KeetaNetJNI.blockBuilderSetSigner(getNativePtr(), signer.getNativePtr());
            updateHandle(builderPtr);
            if (builderPtr == 0) {
                throw new RuntimeException("Failed to set signer");
            }

            return this;
        }

        public Builder purpose(int purpose) {
            if (closed) {
                throw new IllegalStateException("Builder has been closed");
            }
            if (purpose != PURPOSE_GENERIC && purpose != PURPOSE_FEE) {
                throw new IllegalArgumentException("Invalid block purpose: " + purpose);
            }
            long builderPtr = KeetaNetJNI.blockBuilderSetPurpose(getNativePtr(), purpose);
            updateHandle(builderPtr);
            if (builderPtr == 0) {
                throw new RuntimeException("Failed to set block purpose");
            }
            return this;
        }

        /**
         * Set a multisig signer for this block: the multisig address plus
         * the member accounts producing signatures (may be a quorum subset).
         *
         * @param multisig Multisig identifier account
         * @param signers Member accounts that will sign (must hold private keys)
         * @return this builder
         */
        public Builder signer(Account multisig, Account[] signers) {
            if (closed) {
                throw new IllegalStateException("Builder has been closed");
            }
            
            long[] signerPtrs = new long[signers.length];
            for (int i = 0; i < signers.length; i++) {
                signerPtrs[i] = signers[i].getNativePtr();
            }
            
            long builderPtr = KeetaNetJNI.blockBuilderSetMultisigSigner(
                getNativePtr(),
                multisig.getNativePtr(),
                signerPtrs
            );
            updateHandle(builderPtr);
            
            if (builderPtr == 0) {
                throw new RuntimeException("Failed to set multisig signer");
            }
            
            return this;
        }
        
        /**
         * Add an operation to the block
         * 
         * @param operation Operation handle (see Account operation helpers)
         * @return this builder
         */
        public Builder addOperation(Operation operation) {
            if (closed) {
                throw new IllegalStateException("Builder has been closed");
            }
            
            long builderPtr = KeetaNetJNI.blockBuilderAddOperation(getNativePtr(), operation.getNativePtr());
            updateHandle(builderPtr);
            if (builderPtr == 0) {
                throw new RuntimeException("Failed to add operation");
            }
            
            return this;
        }
        
        /**
         * Build, validate and seal the block (creates an unsigned block
         * ready for signing). Consumes this builder.
         * 
         * @return UnsignedBlock ready for signing
         */
        public UnsignedBlock seal() {
            if (closed) {
                throw new IllegalStateException("Builder has been closed");
            }
            
            long unsignedPtr = KeetaNetJNI.blockBuilderBuild(getNativePtr());
            KeetaNetJNI.unregisterNativeHandle("builder", builderHandle);
            closed = true;
            builderHandle = null;

            if (unsignedPtr == 0) {
                throw new RuntimeException("Failed to build block (validation failed?)");
            }
            
            return new UnsignedBlock(unsignedPtr);
        }
        
        @Override
        public void close() {
            if (!closed && builderHandle != null) {
                KeetaNetJNI.freeBlockBuilder(getNativePtr());
                KeetaNetJNI.unregisterNativeHandle("builder", builderHandle);
                builderHandle = null;
                closed = true;
            }
        }

        private long getNativePtr() {
            if (closed || builderHandle == null) {
                throw new IllegalStateException("Builder has been closed");
            }
            return KeetaNetJNI.requireNativeHandle("builder", builderHandle);
        }

        private void updateHandle(long nextPtr) {
            String oldHandle = builderHandle;
            if (nextPtr == 0) {
                KeetaNetJNI.unregisterNativeHandle("builder", oldHandle);
                builderHandle = null;
                closed = true;
                return;
            }
            String nextHandle = KeetaNetJNI.registerNativeHandle("builder", nextPtr);
            builderHandle = nextHandle;
            KeetaNetJNI.unregisterNativeHandle("builder", oldHandle);
        }
    }
    
    /**
     * Unsigned block ready for signing
     */
    public static class UnsignedBlock implements AutoCloseable {
        private String unsignedHandle;
        private boolean freed = false;
        
        private UnsignedBlock(long ptr) {
            this.unsignedHandle = KeetaNetJNI.registerNativeHandle("unsigned-block", ptr);
        }
        
        /**
         * Get the block hash that the signers sign
         * 
         * @return 32-byte block hash
         */
        public byte[] hash() {
            if (freed) {
                throw new IllegalStateException("Block has been freed");
            }
            byte[] hash = KeetaNetJNI.unsignedBlockGetHash(getNativePtr());
            if (hash == null || hash.length == 0) {
                throw new RuntimeException("Failed to compute block hash");
            }
            return hash;
        }

        /**
         * Get the block hash as the canonical uppercase hex string produced by
         * the Rust {@code BlockHash} formatter.
         */
        public String getHashHex() {
            if (freed) {
                throw new IllegalStateException("Block has been freed");
            }
            String hash = KeetaNetJNI.unsignedBlockGetHashString(getNativePtr());
            if (hash == null) {
                throw new RuntimeException("Failed to compute block hash");
            }
            return hash;
        }
        
        /**
         * Get the list of required signers (flattened from the multisig
         * signer tree, in signature order)
         * 
         * @return List of public keys (with key-type prefix) that must sign
         */
        public List<byte[]> getRequiredSigners() {
            if (freed) {
                throw new IllegalStateException("Block has been freed");
            }
            
            byte[] signersData = KeetaNetJNI.unsignedBlockGetSigners(getNativePtr());
            if (signersData == null || signersData.length < 4) {
                throw new RuntimeException("Failed to get required signers");
            }
            List<byte[]> signers = new ArrayList<>();
            
            // Parse: count (4 bytes) + (length (4 bytes) + pubkey) * count
            ByteBuffer buffer = ByteBuffer.wrap(signersData);
            buffer.order(ByteOrder.BIG_ENDIAN);
            
            int count = buffer.getInt();
            if (count < 0) {
                throw new RuntimeException("Invalid required signer count: " + count);
            }
            for (int i = 0; i < count; i++) {
                if (buffer.remaining() < 4) {
                    throw new RuntimeException("Malformed signer payload: missing length for signer " + i);
                }
                int length = buffer.getInt();
                if (length < 0 || buffer.remaining() < length) {
                    throw new RuntimeException("Malformed signer payload: invalid signer length " + length + " at index " + i);
                }
                byte[] pubkey = new byte[length];
                buffer.get(pubkey);
                signers.add(pubkey);
            }
            if (buffer.hasRemaining()) {
                throw new RuntimeException("Malformed signer payload: trailing bytes");
            }
            
            return signers;
        }
        
        /**
         * Sign the block with the private keys held by its required signer
         * accounts (set via the builder's account/signer methods) and seal
         * it. The signatures are also verified during sealing. Consumes
         * this unsigned block.
         * 
         * @return SignedBlock
         */
        public SignedBlock sign() {
            if (freed) {
                throw new IllegalStateException("Block has been freed");
            }
            
            long signedPtr = KeetaNetJNI.unsignedBlockSign(getNativePtr());
            KeetaNetJNI.unregisterNativeHandle("unsigned-block", unsignedHandle);
            freed = true;
            unsignedHandle = null;
            
            if (signedPtr == 0) {
                throw new RuntimeException("Failed to sign block");
            }
            
            return new SignedBlock(signedPtr);
        }
        
        @Override
        public void close() {
            if (!freed && unsignedHandle != null) {
                KeetaNetJNI.freeUnsignedBlock(getNativePtr());
                KeetaNetJNI.unregisterNativeHandle("unsigned-block", unsignedHandle);
                unsignedHandle = null;
                freed = true;
            }
        }

        private long getNativePtr() {
            if (freed || unsignedHandle == null) {
                throw new IllegalStateException("Block has been freed");
            }
            return KeetaNetJNI.requireNativeHandle("unsigned-block", unsignedHandle);
        }
    }
    
    /**
     * Signed block ready for network transmission
     */
    public static class SignedBlock implements AutoCloseable {
        private String signedHandle;
        private boolean freed = false;
        
        private SignedBlock(long ptr) {
            this.signedHandle = KeetaNetJNI.registerNativeHandle("signed-block", ptr);
        }
        
        /**
         * Get the block hash
         * 
         * @return 32-byte block hash
         */
        public byte[] hash() {
            if (freed) {
                throw new IllegalStateException("Block has been freed");
            }
            byte[] hash = KeetaNetJNI.signedBlockGetHash(getNativePtr());
            if (hash == null || hash.length == 0) {
                throw new RuntimeException("Failed to compute block hash");
            }
            return hash;
        }
        
        /**
         * Get block hash as hex string
         * 
         * @return Uppercase hex string
         */
        public String getHashHex() {
            if (freed) {
                throw new IllegalStateException("Block has been freed");
            }
            String hash = KeetaNetJNI.signedBlockGetHashString(getNativePtr());
            if (hash == null) {
                throw new RuntimeException("Failed to compute block hash");
            }
            return hash;
        }
        
        /**
         * Serialize block to bytes (ASN.1/DER) for network transmission
         * 
         * @return Serialized block bytes
         */
        public byte[] toBytes() {
            if (freed) {
                throw new IllegalStateException("Block has been freed");
            }
            byte[] bytes = KeetaNetJNI.signedBlockToBytes(getNativePtr());
            if (bytes == null || bytes.length == 0) {
                throw new RuntimeException("Failed to serialize block");
            }
            return bytes;
        }

        String getAccountPublicKeyString() {
            if (freed) {
                throw new IllegalStateException("Block has been freed");
            }
            String account = KeetaNetJNI.signedBlockGetAccountString(getNativePtr());
            if (account == null) {
                throw new RuntimeException("Failed to get block account");
            }
            return account;
        }

        long getNativePtr() {
            if (freed) {
                throw new IllegalStateException("Block has been freed");
            }
            return KeetaNetJNI.requireNativeHandle("signed-block", signedHandle);
        }
        
        @Override
        public void close() {
            if (!freed && signedHandle != null) {
                KeetaNetJNI.freeSignedBlock(getNativePtr());
                KeetaNetJNI.unregisterNativeHandle("signed-block", signedHandle);
                signedHandle = null;
                freed = true;
            }
        }
    }
}
