package network.keeta.examples;

/**
 * A block operation (CREATE_IDENTIFIER, MODIFY_PERMISSIONS, SET_INFO, ...)
 * held as a native handle. Add to a block with {@link Block.Builder#addOperation}.
 */
public class Operation implements AutoCloseable {
    private long nativePtr;
    private boolean freed = false;

    Operation(long ptr) {
        if (ptr == 0) {
            throw new RuntimeException("Failed to create operation");
        }
        this.nativePtr = ptr;
    }

    /**
     * Create a CREATE_IDENTIFIER operation for a 3-signer multisig identifier.
     *
     * @param multisig Multisig identifier account (see
     *                 {@link Account#generateIdentifier})
     * @param signer1  First signer account
     * @param signer2  Second signer account
     * @param signer3  Third signer account
     * @param quorum   Number of signatures required
     * @return Operation handle (close when no longer needed)
     */
    public static Operation createMultisigIdentifier(Account multisig, Account signer1, Account signer2,
                                                     Account signer3, int quorum) {
        return new Operation(KeetaNetJNI.createMultisigOperation(
            multisig.getNativePtr(),
            signer1.getNativePtr(),
            signer2.getNativePtr(),
            signer3.getNativePtr(),
            quorum
        ));
    }

    /**
     * Create a MODIFY_PERMISSIONS operation.
     *
     * @param principal       Account to grant/revoke permissions to/from
     * @param permissionsBits Permission bits to modify (see {@link Permissions})
     * @param adjustMethod    0=ADD, 1=SUBTRACT, 2=SET
     * @return Operation handle (close when no longer needed)
     */
    public static Operation modifyPermissions(Account principal, long permissionsBits, int adjustMethod) {
        return new Operation(KeetaNetJNI.createModifyPermissionsOperation(
            principal.getNativePtr(),
            permissionsBits,
            adjustMethod
        ));
    }

    /**
     * Create a SET_INFO operation.
     *
     * @param name                  Token name
     * @param description           Token description
     * @param metadata              Base64-encoded metadata string
     * @param defaultPermissionBits Default permission bits (0 = no default permission)
     * @return Operation handle (close when no longer needed)
     */
    public static Operation setInfo(String name, String description, String metadata, long defaultPermissionBits) {
        return new Operation(KeetaNetJNI.createSetInfoOperation(name, description, metadata, defaultPermissionBits));
    }

    long getNativePtr() {
        if (freed) {
            throw new IllegalStateException("Operation has been freed");
        }
        return nativePtr;
    }

    @Override
    public void close() {
        if (!freed && nativePtr != 0) {
            KeetaNetJNI.freeOperation(nativePtr);
            nativePtr = 0;
            freed = true;
        }
    }
}
