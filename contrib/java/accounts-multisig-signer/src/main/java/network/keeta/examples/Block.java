package network.keeta.examples;

import java.util.ArrayList;
import java.util.List;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;

/**
 * Block builder for creating and signing Keetanet blocks
 * 
 * Provides a fluent API for constructing blocks, similar to the TypeScript Block.Builder
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
            
            // Set previous hash or opening hash
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
         * Set the signer for this block (for multisig scenarios)
         * 
         * @param multisig Multisig account
         * @param signers Array of signer accounts
         * @return this builder
         */
        public Builder signer(Account multisig, Account[] signers) {
            if (closed) {
                throw new IllegalStateException("Builder has been closed");
            }
            
            // Convert signer pointers to byte array
            ByteBuffer buffer = ByteBuffer.allocate(signers.length * 8);
            buffer.order(ByteOrder.nativeOrder());
            for (Account signer : signers) {
                buffer.putLong(signer.getNativePtr());
            }
            
            this.builderPtr = KeetaNetJNI.blockBuilderSetMultisigSigner(
                this.builderPtr,
                multisig.getNativePtr(),
                buffer.array()
            );
            
            if (this.builderPtr == 0) {
                throw new RuntimeException("Failed to set multisig signer");
            }
            
            return this;
        }
        
        /**
         * Add an operation to the block
         * 
         * @param operationDer DER-encoded operation bytes
         * @return this builder
         */
        public Builder addOperation(byte[] operationDer) {
            if (closed) {
                throw new IllegalStateException("Builder has been closed");
            }
            
            this.builderPtr = KeetaNetJNI.blockBuilderAddOperation(this.builderPtr, operationDer);
            if (this.builderPtr == 0) {
                throw new RuntimeException("Failed to add operation");
            }
            
            return this;
        }
        
        /**
         * Build and seal the block (creates unsigned block ready for signing)
         * 
         * @return UnsignedBlock ready for signing
         */
        public UnsignedBlock seal() {
            if (closed) {
                throw new IllegalStateException("Builder has been closed");
            }
            
            long unsignedPtr = KeetaNetJNI.blockBuilderBuild(this.builderPtr);
            if (unsignedPtr == 0) {
                throw new RuntimeException("Failed to build block");
            }
            
            closed = true;
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
         * Get the block hash that needs to be signed
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
         * Get the list of required signers (flattened from multisig tree)
         * 
         * @return List of public keys that need to sign
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
         * Sign the block with a single account
         * 
         * @param account Account to sign with
         * @return SignedBlock
         */
        public SignedBlock sign(Account account) {
            if (freed) {
                throw new IllegalStateException("Block has been freed");
            }
            
            byte[] hash = hash();
            byte[] signature = KeetaNetJNI.unsignedBlockSign(unsignedPtr, account.getNativePtr(), hash);
            
            if (signature == null || signature.length != 64) {
                throw new RuntimeException("Failed to sign block (expected 64-byte signature)");
            }
            
            // Package single signature
            ByteBuffer sigBuffer = ByteBuffer.allocate(4 + signature.length);
            sigBuffer.order(ByteOrder.BIG_ENDIAN);
            sigBuffer.putInt(1); // count
            sigBuffer.put(signature);
            
            long signedPtr = KeetaNetJNI.unsignedBlockSeal(unsignedPtr, sigBuffer.array());
            if (signedPtr == 0) {
                throw new RuntimeException("Failed to seal block");
            }
            
            freed = true;
            unsignedPtr = 0;
            
            return new SignedBlock(signedPtr);
        }
        
        /**
         * Sign the block with multiple accounts (for multisig)
         * 
         * @param accounts Array of accounts to sign with (must match required signers)
         * @return SignedBlock
         */
        public SignedBlock signMultisig(Account[] accounts) {
            if (freed) {
                throw new IllegalStateException("Block has been freed");
            }
            
            byte[] hash = hash();
            List<byte[]> signatures = new ArrayList<>();
            
            // Collect signatures from each account
            for (Account account : accounts) {
                byte[] sig = KeetaNetJNI.unsignedBlockSign(unsignedPtr, account.getNativePtr(), hash);
                if (sig == null || sig.length != 64) {
                    throw new RuntimeException("Failed to sign block with account (expected 64-byte signature)");
                }
                signatures.add(sig);
            }
            
            // Package signatures: count (4 bytes) + signature1 (64) + signature2 (64) + ...
            ByteBuffer sigBuffer = ByteBuffer.allocate(4 + signatures.size() * 64);
            sigBuffer.order(ByteOrder.BIG_ENDIAN);
            sigBuffer.putInt(signatures.size());
            for (byte[] sig : signatures) {
                sigBuffer.put(sig);
            }
            
            long signedPtr = KeetaNetJNI.unsignedBlockSeal(unsignedPtr, sigBuffer.array());
            if (signedPtr == 0) {
                throw new RuntimeException("Failed to seal block with multisig");
            }
            
            freed = true;
            unsignedPtr = 0;
            
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
