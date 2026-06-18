package network.keeta.examples;

/**
 * Permission bit masks (1 << BaseFlag) sourced from the Rust
 * keetanetwork_block::BaseFlag enum via JNI.
 */
public final class Permissions {
    // Base permission bit masks — loaded from Rust at class initialisation time.
    // Order matches getPermissionConstants():
    //   [ACCESS, OWNER, ADMIN, UPDATE_INFO, SEND_ON_BEHALF,
    //    TOKEN_CREATE, TOKEN_SUPPLY, TOKEN_BALANCE,
    //    STORAGE_CREATE, STORAGE_HOLD, STORAGE_DEPOSIT,
    //    PERM_ADD, PERM_REMOVE, MANAGE_CERT, MULTISIG_SIGNER]
    public static final long ACCESS;
    public static final long OWNER;
    public static final long ADMIN;
    public static final long UPDATE_INFO;
    public static final long SEND_ON_BEHALF;
    public static final long TOKEN_CREATE;
    public static final long TOKEN_SUPPLY;
    public static final long TOKEN_BALANCE;
    public static final long STORAGE_CREATE;
    public static final long STORAGE_HOLD;
    public static final long STORAGE_DEPOSIT;
    public static final long PERM_ADD;
    public static final long PERM_REMOVE;
    public static final long MANAGE_CERT;
    public static final long MULTISIG_SIGNER;

    static {
        long[] v = KeetaNetJNI.getPermissionConstants();
        if (v == null || v.length < 15) {
            throw new ExceptionInInitializerError(
                "Native getPermissionConstants() returned "
                    + (v == null ? "null" : ("length " + v.length))
                    + ", expected at least 15 entries");
        }
        ACCESS          = v[0];
        OWNER           = v[1];
        ADMIN           = v[2];
        UPDATE_INFO     = v[3];
        SEND_ON_BEHALF  = v[4];
        TOKEN_CREATE    = v[5];
        TOKEN_SUPPLY    = v[6];
        TOKEN_BALANCE   = v[7];
        STORAGE_CREATE  = v[8];
        STORAGE_HOLD    = v[9];
        STORAGE_DEPOSIT = v[10];
        PERM_ADD        = v[11];
        PERM_REMOVE     = v[12];
        MANAGE_CERT     = v[13];
        MULTISIG_SIGNER = v[14];
    }

    private Permissions() {}
}
