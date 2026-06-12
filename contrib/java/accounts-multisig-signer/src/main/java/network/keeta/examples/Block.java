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
    
    /**
     * Builder for creating unsigned blocks
     */
    public static class Builder implements AutoCloseable {
        private long builderPtr;
        private boolean closed = false;
        
        /**
         * Create a new block builder
         * 
         * @param network Network ID
         * @param account Account that owns the block
         * @param previousHash Previous block hash (32 bytes), or null for opening blocks
         */
        public Builder(long network, Account account, byte[] previousHash) {
            this.builderPtr = KeetaNetJNI.createBlockBuilder();
            if (this.builderPtr == 0) {
                throw new RuntimeException("Failed to create block builder");
            }
            
            // Set version to V2 (default)
            this.builderPtr = KeetaNetJNI.blockBuilderSetVersion(this.builderPtr, 2);
            if (this.builderPtr == 0) {
                throw new RuntimeException("Failed to set block version");
            }
            
            // Set network
            this.builderPtr = KeetaNetJNI.blockBuilderSetNetwork(this.builderPtr, network);
            if (this.builderPtr == 0) {
                throw new RuntimeException("Failed to set network");
            }
            
            // Set account
            this.builderPtr = KeetaNetJNI.blockBuilderSetAccount(this.builderPtr, account.getNativePtr());
            if (this.builderPtr == 0) {
                throw new RuntimeException("Failed to set account");
            }
            
            // Set previous hash, or mark as the account opening block
            if (previousHash != null) {
                if (previousHash.length != 32) {
                    throw new IllegalArgumentException("Previous hash must be 32 bytes");
                }
                this.builderPtr = KeetaNetJNI.blockBuilderSetPrevious(this.builderPtr, previousHash);
            } else {
                this.builderPtr = KeetaNetJNI.blockBuilderSetNoPrevious(this.builderPtr);
            }
            
            if (this.builderPtr == 0) {
                throw new RuntimeException("Failed to set previous hash");
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

            this.builderPtr = KeetaNetJNI.blockBuilderSetSigner(this.builderPtr, signer.getNativePtr());
            if (this.builderPtr == 0) {
                throw new RuntimeException("Failed to set signer");
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
            
            this.builderPtr = KeetaNetJNI.blockBuilderSetMultisigSigner(
                this.builderPtr,
                multisig.getNativePtr(),
                signerPtrs
            );
            
            if (this.builderPtr == 0) {
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
            
            this.builderPtr = KeetaNetJNI.blockBuilderAddOperation(this.builderPtr, operation.getNativePtr());
            if (this.builderPtr == 0) {
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
            
            long unsignedPtr = KeetaNetJNI.blockBuilderBuild(this.builderPtr);
            closed = true;
            builderPtr = 0;

            if (unsignedPtr == 0) {
                throw new RuntimeException("Failed to build block (validation failed?)");
            }
            
            return new UnsignedBlock(unsignedPtr);
        }
        
        @Override
        public void close() {
            if (!closed && builderPtr != 0) {
                KeetaNetJNI.freeBlockBuilder(builderPtr);
                builderPtr = 0;
                closed = true;
            }
        }
    }
    
    /**
     * Unsigned block ready for signing
     */
    public static class UnsignedBlock implements AutoCloseable {
        private long unsignedPtr;
        private boolean freed = false;
        
        private UnsignedBlock(long ptr) {
            this.unsignedPtr = ptr;
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
            byte[] hash = KeetaNetJNI.unsignedBlockGetHash(unsignedPtr);
            if (hash == null || hash.length == 0) {
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
            
            byte[] signersData = KeetaNetJNI.unsignedBlockGetSigners(unsignedPtr);
            List<byte[]> signers = new ArrayList<>();
            
            // Parse: count (4 bytes) + (length (4 bytes) + pubkey) * count
            ByteBuffer buffer = ByteBuffer.wrap(signersData);
            buffer.order(ByteOrder.BIG_ENDIAN);
            
            int count = buffer.getInt();
            for (int i = 0; i < count; i++) {
                int length = buffer.getInt();
                byte[] pubkey = new byte[length];
                buffer.get(pubkey);
                signers.add(pubkey);
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
            
            long signedPtr = KeetaNetJNI.unsignedBlockSign(unsignedPtr);
            freed = true;
            unsignedPtr = 0;
            
            if (signedPtr == 0) {
                throw new RuntimeException("Failed to sign block");
            }
            
            return new SignedBlock(signedPtr);
        }
        
        @Override
        public void close() {
            if (!freed && unsignedPtr != 0) {
                KeetaNetJNI.freeUnsignedBlock(unsignedPtr);
                unsignedPtr = 0;
                freed = true;
            }
        }
    }
    
    /**
     * Signed block ready for network transmission
     */
    public static class SignedBlock implements AutoCloseable {
        private long signedPtr;
        private boolean freed = false;
        
        private SignedBlock(long ptr) {
            this.signedPtr = ptr;
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
            byte[] hash = KeetaNetJNI.signedBlockGetHash(signedPtr);
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
            byte[] hash = hash();
            StringBuilder sb = new StringBuilder();
            for (byte b : hash) {
                sb.append(String.format("%02X", b));
            }
            return sb.toString();
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
            byte[] bytes = KeetaNetJNI.signedBlockToBytes(signedPtr);
            if (bytes == null || bytes.length == 0) {
                throw new RuntimeException("Failed to serialize block");
            }
            return bytes;
        }
        
        @Override
        public void close() {
            if (!freed && signedPtr != 0) {
                KeetaNetJNI.freeSignedBlock(signedPtr);
                signedPtr = 0;
                freed = true;
            }
        }
    }
}
