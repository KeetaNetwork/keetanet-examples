use jni::JNIEnv;
use jni::objects::{JByteArray, JClass, JString};
use jni::sys::{jint, jlong, jlongArray, jstring};
use std::ptr;
use zeroize::Zeroize;

// Import keetanetwork crates
use keetanetwork_account::{Account, GenericAccount, KeyPairType};
use keetanetwork_crypto::prelude::*;
use keetanetwork_block::{CreateIdentifierOp, CreateIdentifierArgs, MultisigArgs, ModifyPermissionsOp, SetInfoOp, AdjustMethod, Permission, Operation, NullOr};
use keetanetwork_block::builder::{BlockBuilder, BlockHash, UnsignedBlock, SignedBlock, SignerField, MultisigSignerInfo};
use keetanetwork_block::BlockVersion;
use keetanetwork_block::permissions;
use der::{Encode as DerEncode, asn1::{OctetStringRef, Utf8StringRef}};

#[no_mangle]
pub extern "system" fn Java_network_keeta_examples_KeetaNetJNI_generateRandomSeed(
    env: JNIEnv,
    _class: JClass,
) -> jstring {
    // Generate random seed using keetanetwork-crypto
    match keetanetwork_crypto::utils::generate_random_seed() {
        Ok(seed) => {
            let seed_hex = hex::encode(seed.expose_secret());
            match env.new_string(seed_hex) {
                Ok(jstr) => jstr.into_raw(),
                Err(_) => ptr::null_mut(),
            }
        }
        Err(_) => ptr::null_mut(),
    }
}

/// Returns account key-type constants derived from the `KeyPairType` enum.
/// Order: [ECDSA_SECP256K1, ED25519, NETWORK, TOKEN, STORAGE, ECDSA_SECP256R1, MULTISIG]
#[no_mangle]
pub extern "system" fn Java_network_keeta_examples_KeetaNetJNI_getAccountTypeConstants(
    env: JNIEnv,
    _class: JClass,
) -> jlongArray {
    let values: [i64; 7] = [
        KeyPairType::ECDSASECP256K1 as i64,
        KeyPairType::ED25519        as i64,
        KeyPairType::NETWORK        as i64,
        KeyPairType::TOKEN          as i64,
        KeyPairType::STORAGE        as i64,
        KeyPairType::ECDSASECP256R1 as i64,
        KeyPairType::MULTISIG       as i64,
    ];
    match env.new_long_array(values.len() as i32) {
        Ok(arr) => {
            let _ = env.set_long_array_region(&arr, 0, &values);
            arr.into_raw()
        }
        Err(_) => ptr::null_mut(),
    }
}

/// Returns permission-bit constants from `keetanetwork_block::permissions`.
/// Order: [ACCESS, OWNER, ADMIN, UPDATE_INFO, SEND_ON_BEHALF,
///         TOKEN_CREATE, TOKEN_SUPPLY, TOKEN_BALANCE,
///         STORAGE_CREATE, STORAGE_HOLD, STORAGE_DEPOSIT,
///         PERM_ADD, PERM_REMOVE, MANAGE_CERT, MULTISIG_SIGNER]
#[no_mangle]
pub extern "system" fn Java_network_keeta_examples_KeetaNetJNI_getPermissionConstants(
    env: JNIEnv,
    _class: JClass,
) -> jlongArray {
    let values: [i64; 15] = [
        permissions::ACCESS          as i64,
        permissions::OWNER           as i64,
        permissions::ADMIN           as i64,
        permissions::UPDATE_INFO     as i64,
        permissions::SEND_ON_BEHALF  as i64,
        permissions::TOKEN_CREATE    as i64,
        permissions::TOKEN_SUPPLY    as i64,
        permissions::TOKEN_BALANCE   as i64,
        permissions::STORAGE_CREATE  as i64,
        permissions::STORAGE_HOLD    as i64,
        permissions::STORAGE_DEPOSIT as i64,
        permissions::PERM_ADD        as i64,
        permissions::PERM_REMOVE     as i64,
        permissions::MANAGE_CERT     as i64,
        permissions::MULTISIG_SIGNER as i64,
    ];
    match env.new_long_array(values.len() as i32) {
        Ok(arr) => {
            let _ = env.set_long_array_region(&arr, 0, &values);
            arr.into_raw()
        }
        Err(_) => ptr::null_mut(),
    }
}


#[no_mangle]
pub extern "system" fn Java_network_keeta_examples_KeetaNetJNI_accountFromSeed(
    mut env: JNIEnv,
    _class: JClass,
    seed_hex: JString,
    index: jint,
    key_type: jint,
) -> jlong {
    let seed_str: String = match env.get_string(&seed_hex) {
        Ok(s) => s.into(),
        Err(_) => return 0,
    };

    let mut seed_bytes = match hex::decode(seed_str) {
        Ok(b) => b,
        Err(_) => return 0,
    };

    if seed_bytes.len() != 32 {
        return 0;
    }

    let mut seed_array = [0u8; 32];
    seed_array.copy_from_slice(&seed_bytes);
    seed_bytes.zeroize();
    
    // Combine seed and index into 36-byte array (seed:32 + index:4)
    let mut indexed_seed = [0u8; 36];
    indexed_seed[..32].copy_from_slice(&seed_array);
    indexed_seed[32] = (index >> 24) as u8;
    indexed_seed[33] = (index >> 16) as u8;
    indexed_seed[34] = (index >> 8) as u8;
    indexed_seed[35] = index as u8;
    
    seed_array.zeroize();
    
    let seed = SecretBox::new(Box::new(indexed_seed));
    
    // Map key_type to KeyPairType - based on TypeScript AccountKeyAlgorithm enum
    let keypair_type = match key_type {
        0 => KeyPairType::ECDSASECP256K1,
        1 => KeyPairType::ED25519,
        6 => KeyPairType::ECDSASECP256R1,
        _ => return 0, // Invalid type for cryptographic accounts
    };
    
    // Derive private key from seed using appropriate algorithm  
    let account: GenericAccount = match keypair_type {
        KeyPairType::ED25519 => {
            use keetanetwork_crypto::algorithms::ed25519::Ed25519Derivation;
            match Ed25519Derivation::derive_from_seed(seed) {
                Ok(private_key) => {
                    GenericAccount::Ed25519(Account::<keetanetwork_account::KeyED25519>::from(private_key))
                }
                Err(_) => return 0,
            }
        }
        KeyPairType::ECDSASECP256K1 => {
            use keetanetwork_crypto::algorithms::secp256k1::Secp256k1Derivation;
            match Secp256k1Derivation::derive_from_seed(seed) {
                Ok(private_key) => {
                    GenericAccount::EcdsaSecp256k1(Account::<keetanetwork_account::KeyECDSASECP256K1>::from(private_key))
                }
                Err(_) => return 0,
            }
        }
        KeyPairType::ECDSASECP256R1 => {
            use keetanetwork_crypto::algorithms::secp256r1::Secp256r1Derivation;
            match Secp256r1Derivation::derive_from_seed(seed) {
                Ok(private_key) => {
                    GenericAccount::EcdsaSecp256r1(Account::<keetanetwork_account::KeyECDSASECP256R1>::from(private_key))
                }
                Err(_) => return 0,
            }
        }
        _ => return 0,
    };
    
    // Box it and return as pointer
    Box::into_raw(Box::new(account)) as jlong
}

#[no_mangle]
pub extern "system" fn Java_network_keeta_examples_KeetaNetJNI_getAccountPublicKey(
    env: JNIEnv,
    _class: JClass,
    account_ptr: jlong,
) -> jstring {
    if account_ptr == 0 {
        return ptr::null_mut();
    }

    let account = unsafe { &*(account_ptr as *const GenericAccount) };
    // Use the to_string() method which returns the keeta_ prefixed address
    let pubkey_str = account.to_string();

    match env.new_string(pubkey_str) {
        Ok(jstr) => jstr.into_raw(),
        Err(_) => ptr::null_mut(),
    }
}

#[no_mangle]
pub extern "system" fn Java_network_keeta_examples_KeetaNetJNI_generateMultisigIdentifier(
    _env: JNIEnv,
    _class: JClass,
    account_ptr: jlong,
    operation_index: jint,
) -> jlong {
    if account_ptr == 0 {
        return 0;
    }

    let account = unsafe { &*(account_ptr as *const GenericAccount) };
    
    // GenericAccount doesn't have generate_identifier, need to match on variant
    let identifier = match account {
        GenericAccount::Ed25519(acc) => acc.generate_identifier(KeyPairType::MULTISIG, None, operation_index as u32),
        GenericAccount::EcdsaSecp256k1(acc) => acc.generate_identifier(KeyPairType::MULTISIG, None, operation_index as u32),
        GenericAccount::EcdsaSecp256r1(acc) => acc.generate_identifier(KeyPairType::MULTISIG, None, operation_index as u32),
        _ => return 0, // Identifiers can't generate identifiers
    };
    
    match identifier {
        Ok(id) => Box::into_raw(Box::new(id)) as jlong,
        Err(_) => 0,
    }
}

#[no_mangle]
pub extern "system" fn Java_network_keeta_examples_KeetaNetJNI_generateTokenIdentifier(
    _env: JNIEnv,
    _class: JClass,
    account_ptr: jlong,
    operation_index: jint,
) -> jlong {
    if account_ptr == 0 {
        return 0;
    }

    let account = unsafe { &*(account_ptr as *const GenericAccount) };

    let identifier = match account {
        GenericAccount::Ed25519(acc) => acc.generate_identifier(KeyPairType::TOKEN, None, operation_index as u32),
        GenericAccount::EcdsaSecp256k1(acc) => acc.generate_identifier(KeyPairType::TOKEN, None, operation_index as u32),
        GenericAccount::EcdsaSecp256r1(acc) => acc.generate_identifier(KeyPairType::TOKEN, None, operation_index as u32),
        _ => return 0,
    };

    match identifier {
        Ok(id) => Box::into_raw(Box::new(id)) as jlong,
        Err(_) => 0,
    }
}

#[no_mangle]
pub extern "system" fn Java_network_keeta_examples_KeetaNetJNI_signMessage<'local>(
    env: JNIEnv<'local>,
    _class: JClass,
    account_ptr: jlong,
    message: JByteArray<'local>,
) -> JByteArray<'local> {
    if account_ptr == 0 {
        return JByteArray::default();
    }

    let account = unsafe { &*(account_ptr as *const GenericAccount) };
    
    let message_bytes = match env.convert_byte_array(&message) {
        Ok(bytes) => bytes,
        Err(_) => return JByteArray::default(),
    };
    
    // Match on GenericAccount variant to call sign
    let signature = match account {
        GenericAccount::Ed25519(acc) => acc.sign(&message_bytes, None),
        GenericAccount::EcdsaSecp256k1(acc) => acc.sign(&message_bytes, None),
        GenericAccount::EcdsaSecp256r1(acc) => acc.sign(&message_bytes, None),
        _ => return JByteArray::default(), // Identifiers can't sign
    };
    
    match signature {
        Ok(sig) => {
            match env.byte_array_from_slice(&sig) {
                Ok(arr) => arr,
                Err(_) => JByteArray::default(),
            }
        }
        Err(_) => JByteArray::default(),
    }
}

#[no_mangle]
pub extern "system" fn Java_network_keeta_examples_KeetaNetJNI_verifySignature(
    env: JNIEnv,
    _class: JClass,
    account_ptr: jlong,
    message: JByteArray,
    signature: JByteArray,
) -> jint {
    if account_ptr == 0 {
        return 0;
    }

    let account = unsafe { &*(account_ptr as *const GenericAccount) };
    
    let message_bytes = match env.convert_byte_array(&message) {
        Ok(bytes) => bytes,
        Err(_) => return 0,
    };
    
    let signature_bytes = match env.convert_byte_array(&signature) {
        Ok(bytes) => bytes,
        Err(_) => return 0,
    };
    
    // Match on GenericAccount variant to call verify
    let result = match account {
        GenericAccount::Ed25519(acc) => acc.verify(&message_bytes, &signature_bytes, None),
        GenericAccount::EcdsaSecp256k1(acc) => acc.verify(&message_bytes, &signature_bytes, None),
        GenericAccount::EcdsaSecp256r1(acc) => acc.verify(&message_bytes, &signature_bytes, None),
        _ => return 0, // Identifiers can't verify
    };
    
    match result {
        Ok(_) => 1, // Valid
        Err(_) => 0, // Invalid
    }
}

#[no_mangle]
pub extern "system" fn Java_network_keeta_examples_KeetaNetJNI_freeAccount(
    _env: JNIEnv,
    _class: JClass,
    account_ptr: jlong,
) {
    if account_ptr != 0 {
        unsafe {
            let _ = Box::from_raw(account_ptr as *mut GenericAccount);
        }
    }
}

// Helper function to get account type
#[no_mangle]
pub extern "system" fn Java_network_keeta_examples_KeetaNetJNI_getAccountType(
    _env: JNIEnv,
    _class: JClass,
    account_ptr: jlong,
) -> jint {
    if account_ptr == 0 {
        return -1;
    }

    let account = unsafe { &*(account_ptr as *const GenericAccount) };
    account.to_keypair_type() as jint
}

// Block construction functions

/// Encode signers as a DER `SEQUENCE OF OCTET STRING` using the `der` crate
/// (a `Vec<T: Encode>` encodes as SEQUENCE OF).
fn encode_signers_der(signers: &[&[u8]]) -> Result<Vec<u8>, der::Error> {
    let octets: Vec<OctetStringRef> = signers
        .iter()
        .map(|s| OctetStringRef::new(s))
        .collect::<Result<_, _>>()?;
    octets.to_der()
}

#[no_mangle]
pub extern "system" fn Java_network_keeta_examples_KeetaNetJNI_createMultisigOperation<'local>(
    env: JNIEnv<'local>,
    _class: JClass,
    multisig_ptr: jlong,
    signer1_ptr: jlong,
    signer2_ptr: jlong,
    signer3_ptr: jlong,
    quorum: jint,
) -> JByteArray<'local> {
    if multisig_ptr == 0 {
        return JByteArray::default();
    }

    let multisig = unsafe { &*(multisig_ptr as *const GenericAccount) };
    let signer1 = unsafe { &*(signer1_ptr as *const GenericAccount) };
    let signer2 = unsafe { &*(signer2_ptr as *const GenericAccount) };
    let signer3 = unsafe { &*(signer3_ptr as *const GenericAccount) };
    
    // Get public key with type prefix
    let multisig_bytes_vec = multisig.to_public_key_with_type();
    
    let multisig_octet = match OctetStringRef::new(&multisig_bytes_vec) {
        Ok(o) => o,
        Err(_) => return JByteArray::default(),
    };
    
    // Get signer public keys
    let get_pubkey_vec = |acc: &GenericAccount| -> Option<Vec<u8>> {
        Some(acc.to_public_key_with_type())
    };
    
    let signer1_bytes = match get_pubkey_vec(signer1) {
        Some(b) => b,
        None => return JByteArray::default(),
    };
    let signer2_bytes = match get_pubkey_vec(signer2) {
        Some(b) => b,
        None => return JByteArray::default(),
    };
    let signer3_bytes = match get_pubkey_vec(signer3) {
        Some(b) => b,
        None => return JByteArray::default(),
    };
    
    // Encode signers as DER SEQUENCE OF OCTET STRING
    let signers_der = match encode_signers_der(&[
        signer1_bytes.as_slice(),
        signer2_bytes.as_slice(),
        signer3_bytes.as_slice(),
    ]) {
        Ok(der) => der,
        Err(_) => return JByteArray::default(),
    };
    
    // Create the operation
    let multisig_args = MultisigArgs {
        signers: &signers_der,
        quorum: quorum as u64,
    };
    
    let create_id_op = CreateIdentifierOp {
        identifier: multisig_octet,
        create_arguments: Some(CreateIdentifierArgs::Multisig(multisig_args)),
    };
    
    let operation = Operation::CreateIdentifier(create_id_op);
    
    // Encode operation to DER
    match operation.to_der() {
        Ok(der_bytes) => {
            match env.byte_array_from_slice(&der_bytes) {
                Ok(arr) => arr,
                Err(_) => JByteArray::default(),
            }
        }
        Err(_) => JByteArray::default(),
    }
}

#[no_mangle]
pub extern "system" fn Java_network_keeta_examples_KeetaNetJNI_createModifyPermissionsOperation<'local>(
    env: JNIEnv<'local>,
    _class: JClass,
    principal_ptr: jlong,
    permissions_base: jlong,
    adjust_method: jint,
) -> JByteArray<'local> {
    if principal_ptr == 0 {
        return JByteArray::default();
    }

    let principal = unsafe { &*(principal_ptr as *const GenericAccount) };
    
    // Get principal public key bytes with algorithm type prefix
    let principal_bytes_vec = principal.to_public_key_with_type();
    
    let principal_octet = match OctetStringRef::new(&principal_bytes_vec) {
        Ok(o) => o,
        Err(_) => return JByteArray::default(),
    };
    
    let method = match adjust_method {
        0 => AdjustMethod::Add,
        1 => AdjustMethod::Subtract,
        2 => AdjustMethod::Set,
        _ => return JByteArray::default(),
    };
    
    let permission = Permission {
        base: permissions_base as u64,
        external: 0, // No external permissions for this example
    };
    
    let mod_perms_op = ModifyPermissionsOp {
        method,
        principal: principal_octet,
        permissions: NullOr::Value(permission),
        target: None, // No specific target, applies to the block's account
    };
    
    let operation = Operation::ModifyPermissions(mod_perms_op);
    
    // Encode operation to DER
    match operation.to_der() {
        Ok(der_bytes) => {
            match env.byte_array_from_slice(&der_bytes) {
                Ok(arr) => arr,
                Err(_) => JByteArray::default(),
            }
        }
        Err(_) => JByteArray::default(),
    }
}

/// Create a SET_INFO operation (DER-encoded)
#[no_mangle]
pub extern "system" fn Java_network_keeta_examples_KeetaNetJNI_createSetInfoOperation<'local>(
    mut env: JNIEnv<'local>,
    _class: JClass,
    name: JString<'local>,
    description: JString<'local>,
    metadata: JString<'local>,
    access_permission_base: jlong,
) -> JByteArray<'local> {
    let name_str: String = match env.get_string(&name) {
        Ok(s) => s.into(),
        Err(_) => return JByteArray::default(),
    };
    let desc_str: String = match env.get_string(&description) {
        Ok(s) => s.into(),
        Err(_) => return JByteArray::default(),
    };
    let meta_str: String = match env.get_string(&metadata) {
        Ok(s) => s.into(),
        Err(_) => return JByteArray::default(),
    };

    let name_ref = match Utf8StringRef::new(&name_str) {
        Ok(s) => s,
        Err(_) => return JByteArray::default(),
    };
    let desc_ref = match Utf8StringRef::new(&desc_str) {
        Ok(s) => s,
        Err(_) => return JByteArray::default(),
    };
    let meta_ref = match Utf8StringRef::new(&meta_str) {
        Ok(s) => s,
        Err(_) => return JByteArray::default(),
    };

    let default_permission = if access_permission_base != 0 {
        Some(Permission { base: access_permission_base as u64, external: 0 })
    } else {
        None
    };

    let set_info_op = SetInfoOp {
        name: name_ref,
        description: desc_ref,
        metadata: meta_ref,
        default_permission,
    };

    let operation = Operation::SetInfo(set_info_op);

    match operation.to_der() {
        Ok(der_bytes) => {
            match env.byte_array_from_slice(&der_bytes) {
                Ok(arr) => arr,
                Err(_) => JByteArray::default(),
            }
        }
        Err(_) => JByteArray::default(),
    }
}

// Block builder functions

/// Create a new block builder
/// Returns a pointer to BlockBuilder
#[no_mangle]
pub extern "system" fn Java_network_keeta_examples_KeetaNetJNI_createBlockBuilder(
    _env: JNIEnv,
    _class: JClass,
) -> jlong {
    let builder = BlockBuilder::new();
    Box::into_raw(Box::new(builder)) as jlong
}

/// Set block version (1 = V1, 2 = V2)
#[no_mangle]
pub extern "system" fn Java_network_keeta_examples_KeetaNetJNI_blockBuilderSetVersion(
    _env: JNIEnv,
    _class: JClass,
    builder_ptr: jlong,
    version: jint,
) -> jlong {
    if builder_ptr == 0 {
        return 0;
    }
    
    let builder = unsafe { Box::from_raw(builder_ptr as *mut BlockBuilder) };
    let version_enum = match version {
        1 => BlockVersion::V1,
        2 => BlockVersion::V2,
        _ => return 0,
    };
    
    let builder = builder.version(version_enum);
    Box::into_raw(Box::new(builder)) as jlong
}

/// Set network ID
#[no_mangle]
pub extern "system" fn Java_network_keeta_examples_KeetaNetJNI_blockBuilderSetNetwork(
    _env: JNIEnv,
    _class: JClass,
    builder_ptr: jlong,
    network: jlong,
) -> jlong {
    if builder_ptr == 0 {
        return 0;
    }
    
    let builder = unsafe { Box::from_raw(builder_ptr as *mut BlockBuilder) };
    let builder = builder.network(network as u64);
    Box::into_raw(Box::new(builder)) as jlong
}

/// Set account public key
#[no_mangle]
pub extern "system" fn Java_network_keeta_examples_KeetaNetJNI_blockBuilderSetAccount(
    _env: JNIEnv,
    _class: JClass,
    builder_ptr: jlong,
    account_ptr: jlong,
) -> jlong {
    if builder_ptr == 0 || account_ptr == 0 {
        return 0;
    }
    
    let builder = unsafe { Box::from_raw(builder_ptr as *mut BlockBuilder) };
    let account = unsafe { &*(account_ptr as *const GenericAccount) };
    
    // Get public key with algorithm type prefix for block encoding
    let account_bytes = account.to_public_key_with_type();
    
    let builder = builder.account(account_bytes);
    Box::into_raw(Box::new(builder)) as jlong
}

/// Set signer (single account)
#[no_mangle]
pub extern "system" fn Java_network_keeta_examples_KeetaNetJNI_blockBuilderSetSigner(
    _env: JNIEnv,
    _class: JClass,
    builder_ptr: jlong,
    signer_ptr: jlong,
) -> jlong {
    if builder_ptr == 0 || signer_ptr == 0 {
        return 0;
    }
    
    let builder = unsafe { Box::from_raw(builder_ptr as *mut BlockBuilder) };
    let signer = unsafe { &*(signer_ptr as *const GenericAccount) };
    
    let signer_bytes = signer.to_public_key_with_type();
    
    let builder = builder.signer(signer_bytes);
    Box::into_raw(Box::new(builder)) as jlong
}

/// Set multisig signer with multiple signing accounts
#[no_mangle]
pub extern "system" fn Java_network_keeta_examples_KeetaNetJNI_blockBuilderSetMultisigSigner(
    env: JNIEnv,
    _class: JClass,
    builder_ptr: jlong,
    multisig_ptr: jlong,
    signer_ptrs: JByteArray,
) -> jlong {
    if builder_ptr == 0 || multisig_ptr == 0 {
        return 0;
    }
    
    let builder = unsafe { Box::from_raw(builder_ptr as *mut BlockBuilder) };
    let multisig = unsafe { &*(multisig_ptr as *const GenericAccount) };
    
    // Get multisig public key with type
    let multisig_bytes = multisig.to_public_key_with_type();
    
    // Parse signer pointers from byte array
    let signer_ptr_bytes = match env.convert_byte_array(&signer_ptrs) {
        Ok(bytes) => bytes,
        Err(_) => return 0,
    };
    
    // Convert bytes to array of longs (8 bytes per pointer)
    let mut signers = Vec::new();
    for chunk in signer_ptr_bytes.chunks_exact(8) {
        let ptr = i64::from_ne_bytes([
            chunk[0], chunk[1], chunk[2], chunk[3],
            chunk[4], chunk[5], chunk[6], chunk[7],
        ]) as jlong;
        
        if ptr != 0 {
            let signer = unsafe { &*(ptr as *const GenericAccount) };
            let signer_bytes = signer.to_public_key_with_type();
            signers.push(SignerField::Account(signer_bytes));
        }
    }
    
    let multisig_info = MultisigSignerInfo {
        multisig_pubkey: multisig_bytes,
        signers,
    };
    
    let builder = builder.multisig_signer(SignerField::Multisig(Box::new(multisig_info)));
    Box::into_raw(Box::new(builder)) as jlong
}

/// Set previous block hash
#[no_mangle]
pub extern "system" fn Java_network_keeta_examples_KeetaNetJNI_blockBuilderSetPrevious(
    env: JNIEnv,
    _class: JClass,
    builder_ptr: jlong,
    previous_hash: JByteArray,
) -> jlong {
    if builder_ptr == 0 {
        return 0;
    }
    
    let builder = unsafe { Box::from_raw(builder_ptr as *mut BlockBuilder) };
    let hash_bytes = match env.convert_byte_array(&previous_hash) {
        Ok(bytes) => bytes,
        Err(_) => return 0,
    };
    
    if hash_bytes.len() != 32 {
        return 0;
    }
    
    let mut hash_array = [0u8; 32];
    hash_array.copy_from_slice(&hash_bytes);
    
    let builder = builder.previous(BlockHash(hash_array));
    Box::into_raw(Box::new(builder)) as jlong
}

/// Set no previous (for opening blocks)
#[no_mangle]
pub extern "system" fn Java_network_keeta_examples_KeetaNetJNI_blockBuilderSetNoPrevious(
    _env: JNIEnv,
    _class: JClass,
    builder_ptr: jlong,
) -> jlong {
    if builder_ptr == 0 {
        return 0;
    }
    
    let builder = unsafe { Box::from_raw(builder_ptr as *mut BlockBuilder) };
    let builder = builder.no_previous();
    Box::into_raw(Box::new(builder)) as jlong
}

/// Add an operation (DER-encoded)
#[no_mangle]
pub extern "system" fn Java_network_keeta_examples_KeetaNetJNI_blockBuilderAddOperation(
    env: JNIEnv,
    _class: JClass,
    builder_ptr: jlong,
    operation_der: JByteArray,
) -> jlong {
    if builder_ptr == 0 {
        return 0;
    }
    
    let builder = unsafe { Box::from_raw(builder_ptr as *mut BlockBuilder) };
    let op_bytes = match env.convert_byte_array(&operation_der) {
        Ok(bytes) => bytes,
        Err(_) => return 0,
    };
    
    // Parse operation from DER
    // We need to leak the bytes to get a 'static lifetime for the operation
    // This is necessary because BlockBuilder requires Operation<'static>
    use der::Decode;
    let op_bytes_static: &'static [u8] = Box::leak(op_bytes.into_boxed_slice());
    let operation: Operation<'static> = match Operation::from_der(op_bytes_static) {
        Ok(op) => op,
        Err(_) => return 0,
    };
    
    let builder = builder.add_operation(operation);
    Box::into_raw(Box::new(builder)) as jlong
}

/// Build unsigned block (ready for signing)
#[no_mangle]
pub extern "system" fn Java_network_keeta_examples_KeetaNetJNI_blockBuilderBuild(
    _env: JNIEnv,
    _class: JClass,
    builder_ptr: jlong,
) -> jlong {
    if builder_ptr == 0 {
        return 0;
    }
    
    let builder = unsafe { Box::from_raw(builder_ptr as *mut BlockBuilder) };
    
    match builder.build() {
        Ok(unsigned) => Box::into_raw(Box::new(unsigned)) as jlong,
        Err(_) => 0,
    }
}

/// Get hash of unsigned block (to be signed)
#[no_mangle]
pub extern "system" fn Java_network_keeta_examples_KeetaNetJNI_unsignedBlockGetHash<'local>(
    env: JNIEnv<'local>,
    _class: JClass,
    unsigned_ptr: jlong,
) -> JByteArray<'local> {
    if unsigned_ptr == 0 {
        return JByteArray::default();
    }
    
    let unsigned = unsafe { &*(unsigned_ptr as *const UnsignedBlock) };
    
    match unsigned.hash() {
        Ok(block_hash) => {
            match env.byte_array_from_slice(block_hash.as_bytes()) {
                Ok(arr) => arr,
                Err(_) => JByteArray::default(),
            }
        }
        Err(_) => JByteArray::default(),
    }
}

/// Get required signers from unsigned block
#[no_mangle]
pub extern "system" fn Java_network_keeta_examples_KeetaNetJNI_unsignedBlockGetSigners<'local>(
    env: JNIEnv<'local>,
    _class: JClass,
    unsigned_ptr: jlong,
) -> JByteArray<'local> {
    if unsigned_ptr == 0 {
        return JByteArray::default();
    }
    
    let unsigned = unsafe { &*(unsigned_ptr as *const UnsignedBlock) };
    let signers = unsigned.signer.get_sorted_signers();
    
    // Serialize as: count (4 bytes) + (length (4 bytes) + pubkey) * count
    let mut result = Vec::new();
    result.extend_from_slice(&(signers.len() as u32).to_be_bytes());
    
    for signer in signers {
        result.extend_from_slice(&(signer.len() as u32).to_be_bytes());
        result.extend_from_slice(&signer);
    }
    
    match env.byte_array_from_slice(&result) {
        Ok(arr) => arr,
        Err(_) => JByteArray::default(),
    }
}

/// Sign block with account's private key
#[no_mangle]
pub extern "system" fn Java_network_keeta_examples_KeetaNetJNI_unsignedBlockSign<'local>(
    env: JNIEnv<'local>,
    _class: JClass,
    unsigned_ptr: jlong,
    account_ptr: jlong,
    block_hash: JByteArray<'local>,
) -> JByteArray<'local> {
    if unsigned_ptr == 0 || account_ptr == 0 {
        return JByteArray::default();
    }
    
    let account = unsafe { &*(account_ptr as *const GenericAccount) };
    let hash_bytes = match env.convert_byte_array(&block_hash) {
        Ok(bytes) => bytes,
        Err(_) => return JByteArray::default(),
    };
    
    // Sign the hash
    let signature = match account {
        GenericAccount::Ed25519(acc) => acc.sign(&hash_bytes, None),
        GenericAccount::EcdsaSecp256k1(acc) => acc.sign(&hash_bytes, None),
        GenericAccount::EcdsaSecp256r1(acc) => acc.sign(&hash_bytes, None),
        _ => return JByteArray::default(),
    };
    
    match signature {
        Ok(sig) => {
            match env.byte_array_from_slice(&sig) {
                Ok(arr) => arr,
                Err(_) => JByteArray::default(),
            }
        }
        Err(_) => JByteArray::default(),
    }
}

/// Seal unsigned block with signatures
#[no_mangle]
pub extern "system" fn Java_network_keeta_examples_KeetaNetJNI_unsignedBlockSeal(
    env: JNIEnv,
    _class: JClass,
    unsigned_ptr: jlong,
    signatures: JByteArray,
) -> jlong {
    if unsigned_ptr == 0 {
        return 0;
    }
    
    let unsigned = unsafe { Box::from_raw(unsigned_ptr as *mut UnsignedBlock) };
    let sig_bytes = match env.convert_byte_array(&signatures) {
        Ok(bytes) => bytes,
        Err(_) => return 0,
    };
    
    // Parse signatures: count (4 bytes) + (signature 64 bytes) * count
    if sig_bytes.len() < 4 {
        return 0;
    }
    
    let count = u32::from_be_bytes([sig_bytes[0], sig_bytes[1], sig_bytes[2], sig_bytes[3]]) as usize;
    let mut parsed_sigs = Vec::new();
    
    let mut offset = 4;
    for _ in 0..count {
        if offset + 64 > sig_bytes.len() {
            return 0;
        }
        
        let mut sig = [0u8; 64];
        sig.copy_from_slice(&sig_bytes[offset..offset + 64]);
        parsed_sigs.push(sig);
        offset += 64;
    }
    
    match unsigned.seal(parsed_sigs) {
        Ok(signed) => Box::into_raw(Box::new(signed)) as jlong,
        Err(_) => 0,
    }
}

/// Get signed block hash
#[no_mangle]
pub extern "system" fn Java_network_keeta_examples_KeetaNetJNI_signedBlockGetHash<'local>(
    env: JNIEnv<'local>,
    _class: JClass,
    signed_ptr: jlong,
) -> JByteArray<'local> {
    if signed_ptr == 0 {
        return JByteArray::default();
    }
    
    let signed = unsafe { &*(signed_ptr as *const SignedBlock) };
    
    match signed.hash() {
        Ok(block_hash) => {
            match env.byte_array_from_slice(block_hash.as_bytes()) {
                Ok(arr) => arr,
                Err(_) => JByteArray::default(),
            }
        }
        Err(_) => JByteArray::default(),
    }
}

/// Serialize signed block to bytes (for network transmission)
#[no_mangle]
pub extern "system" fn Java_network_keeta_examples_KeetaNetJNI_signedBlockToBytes<'local>(
    env: JNIEnv<'local>,
    _class: JClass,
    signed_ptr: jlong,
) -> JByteArray<'local> {
    if signed_ptr == 0 {
        return JByteArray::default();
    }
    
    let signed = unsafe { &*(signed_ptr as *const SignedBlock) };
    
    match signed.to_bytes() {
        Ok(bytes) => {
            match env.byte_array_from_slice(&bytes) {
                Ok(arr) => arr,
                Err(_) => JByteArray::default(),
            }
        }
        Err(_) => JByteArray::default(),
    }
}

/// Free block builder
#[no_mangle]
pub extern "system" fn Java_network_keeta_examples_KeetaNetJNI_freeBlockBuilder(
    _env: JNIEnv,
    _class: JClass,
    builder_ptr: jlong,
) {
    if builder_ptr != 0 {
        unsafe {
            let _ = Box::from_raw(builder_ptr as *mut BlockBuilder);
        }
    }
}

/// Free unsigned block
#[no_mangle]
pub extern "system" fn Java_network_keeta_examples_KeetaNetJNI_freeUnsignedBlock(
    _env: JNIEnv,
    _class: JClass,
    unsigned_ptr: jlong,
) {
    if unsigned_ptr != 0 {
        unsafe {
            let _ = Box::from_raw(unsigned_ptr as *mut UnsignedBlock);
        }
    }
}

/// Free signed block
#[no_mangle]
pub extern "system" fn Java_network_keeta_examples_KeetaNetJNI_freeSignedBlock(
    _env: JNIEnv,
    _class: JClass,
    signed_ptr: jlong,
) {
    if signed_ptr != 0 {
        unsafe {
            let _ = Box::from_raw(signed_ptr as *mut SignedBlock);
        }
    }
}
