package network.keeta.examples;

import java.math.BigInteger;

/**
 * Thin wrapper for the Rust {@code keetanetwork_client::UserClient}.
 * Supports querying account heads/balances and transmitting sealed blocks.
 */
public class UserClient implements AutoCloseable {
    private long nativePtr;
    private boolean freed = false;

    private UserClient(long nativePtr) {
        if (nativePtr == 0) {
            throw new RuntimeException("Failed to create user client");
        }
        this.nativePtr = nativePtr;
    }

    public static UserClient fromNetwork(String networkName, Account signer) {
        return new UserClient(KeetaNetJNI.userClientFromNetwork(networkName, signer == null ? 0 : signer.getNativePtr()));
    }

    public Account getBaseToken() {
        return Account.fromNativePtr(KeetaNetJNI.userClientGetBaseToken(getNativePtr()));
    }

    public BigInteger getBalance(Account account, Account token) {
        String balance = KeetaNetJNI.userClientGetBalance(getNativePtr(), account.getNativePtr(), token.getNativePtr());
        if (balance == null) {
            throw new RuntimeException("Failed to fetch account balance");
        }
        return new BigInteger(balance);
    }

    public byte[] head() {
        byte[] hash = KeetaNetJNI.userClientHead(getNativePtr());
        return normalizeHeadHash(hash);
    }

    public byte[] head(Account account) {
        byte[] hash = KeetaNetJNI.userClientHeadForAccount(getNativePtr(), account.getNativePtr());
        return normalizeHeadHash(hash);
    }

    public boolean transmit(Block.SignedBlock... blocks) {
        long[] ptrs = new long[blocks.length];
        for (int i = 0; i < blocks.length; i++) {
            ptrs[i] = blocks[i].getNativePtr();
        }
        return KeetaNetJNI.userClientTransmit(getNativePtr(), ptrs);
    }

    public Account generateIdentifier(Account.AccountKeyAlgorithm keyType) {
        return Account.fromNativePtr(KeetaNetJNI.userClientGenerateIdentifier(getNativePtr(), keyType.value()));
    }

    public boolean updatePermissions(Account principal, long permissionsBits, int adjustMethod, Account target) {
        long targetPtr = target == null ? 0 : target.getNativePtr();
        return KeetaNetJNI.userClientUpdatePermissions(
            getNativePtr(),
            principal.getNativePtr(),
            permissionsBits,
            adjustMethod,
            targetPtr
        );
    }

    private long getNativePtr() {
        if (freed) {
            throw new IllegalStateException("UserClient has been freed");
        }
        return nativePtr;
    }

    private static byte[] normalizeHeadHash(byte[] hash) {
        if (hash == null || hash.length == 0) {
            return null;
        }
        if (hash.length != 32) {
            throw new RuntimeException("Invalid head hash length: " + hash.length + " (expected 32)");
        }
        return hash;
    }

    @Override
    public void close() {
        if (!freed && nativePtr != 0) {
            KeetaNetJNI.freeUserClient(nativePtr);
            nativePtr = 0;
            freed = true;
        }
    }
}
