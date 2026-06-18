//! JNI shim exposing the keetanetwork Rust crates to Java.
//!
//! Pointer-based handles cross the JNI boundary:
//! - Account handles are `Box<AccountRef>` (`Arc<GenericAccount>`)
//! - Operation handles are `Box<Operation>`
//! - Builder/block handles are `Box<BlockBuilder>` / `Box<UnsignedBlock>` / `Box<Block>`
//!
//! Java owns each handle and must release it through the matching `free*`
//! function (or a consuming call such as `blockBuilderBuild`/`unsignedBlockSign`).

use jni::objects::{JByteArray, JClass, JLongArray, JString};
use jni::sys::{jboolean, jbyteArray, jint, jlong, jlongArray, jstring};
use jni::JNIEnv;
use std::ptr;
use std::str::FromStr;
use std::sync::Arc;
use std::sync::OnceLock;

use keetanetwork_account::account::AccountSigner;
use keetanetwork_account::{Account, Accountable, GenericAccount, KeyNETWORK, KeyPairType, Keyable};
use keetanetwork_block::{
	AccountRef, AdjustMethod, Block, BlockBuilder, BlockHash, BlockVersion, CreateIdentifier, Hashable,
	IdentifierCreateArguments, ModifyPermissions, ModifyPermissionsPrincipal, MultisigCreateArguments, Operation,
	Permissions, SetInfo, Signer, UnsignedBlock,
};
use keetanetwork_block::BaseFlag;
use keetanetwork_client::{Network, TransmitOptions, UserClient};
use keetanetwork_crypto::prelude::*;
use num_bigint::BigInt;
use tokio::runtime::{Builder as TokioRuntimeBuilder, Runtime as TokioRuntime};

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

/// Returns account key-type constants from the `KeyPairType` enum.
/// Order: [ECDSA_SECP256K1, ED25519, NETWORK, TOKEN, STORAGE, ECDSA_SECP256R1, MULTISIG]
#[no_mangle]
pub extern "system" fn Java_network_keeta_examples_KeetaNetJNI_getAccountTypeConstants(
	env: JNIEnv,
	_class: JClass,
) -> jlongArray {
	let values: [i64; 7] = [
		KeyPairType::ECDSASECP256K1 as i64,
		KeyPairType::ED25519 as i64,
		KeyPairType::NETWORK as i64,
		KeyPairType::TOKEN as i64,
		KeyPairType::STORAGE as i64,
		KeyPairType::ECDSASECP256R1 as i64,
		KeyPairType::MULTISIG as i64,
	];
	match env.new_long_array(values.len() as i32) {
		Ok(arr) => {
			let _ = env.set_long_array_region(&arr, 0, &values);
			arr.into_raw()
		}
		Err(_) => ptr::null_mut(),
	}
}

/// Returns base permission bit masks (`1 << BaseFlag`) from
/// `keetanetwork_block::BaseFlag`.
/// Order: [ACCESS, OWNER, ADMIN, UPDATE_INFO, SEND_ON_BEHALF,
///         TOKEN_CREATE, TOKEN_SUPPLY, TOKEN_BALANCE,
///         STORAGE_CREATE, STORAGE_HOLD, STORAGE_DEPOSIT,
///         PERM_ADD, PERM_REMOVE, MANAGE_CERT, MULTISIG_SIGNER]
#[no_mangle]
pub extern "system" fn Java_network_keeta_examples_KeetaNetJNI_getPermissionConstants(
	env: JNIEnv,
	_class: JClass,
) -> jlongArray {
	let flags = [
		BaseFlag::Access,
		BaseFlag::Owner,
		BaseFlag::Admin,
		BaseFlag::UpdateInfo,
		BaseFlag::SendOnBehalf,
		BaseFlag::TokenAdminCreate,
		BaseFlag::TokenAdminSupply,
		BaseFlag::TokenAdminModifyBalance,
		BaseFlag::StorageCreate,
		BaseFlag::StorageCanHold,
		BaseFlag::StorageDeposit,
		BaseFlag::PermissionDelegateAdd,
		BaseFlag::PermissionDelegateRemove,
		BaseFlag::ManageCertificate,
		BaseFlag::MultisigSigner,
	];
	let values: Vec<i64> = flags.iter().map(|flag| 1i64 << (*flag as u8)).collect();
	match env.new_long_array(values.len() as i32) {
		Ok(arr) => {
			let _ = env.set_long_array_region(&arr, 0, &values);
			arr.into_raw()
		}
		Err(_) => ptr::null_mut(),
	}
}

// ---------------------------------------------------------------------------
// Accounts
// ---------------------------------------------------------------------------

#[no_mangle]
pub extern "system" fn Java_network_keeta_examples_KeetaNetJNI_generateRandomSeed(
	env: JNIEnv,
	_class: JClass,
) -> jstring {
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

/// Borrow an account handle.
unsafe fn account_ref(ptr: jlong) -> &'static AccountRef {
	&*(ptr as *const AccountRef)
}

fn account_to_handle(account: GenericAccount) -> jlong {
	Box::into_raw(Box::new(Arc::new(account))) as jlong
}

fn account_ref_to_handle(account: AccountRef) -> jlong {
	Box::into_raw(Box::new(account)) as jlong
}

struct UserClientHandle {
	client: UserClient,
	network: Network,
}

unsafe fn user_client_ref(ptr: jlong) -> &'static UserClientHandle {
	&*(ptr as *const UserClientHandle)
}

fn runtime() -> &'static TokioRuntime {
	static RUNTIME: OnceLock<TokioRuntime> = OnceLock::new();
	RUNTIME.get_or_init(|| {
		TokioRuntimeBuilder::new_multi_thread()
			.enable_all()
			.build()
			.expect("failed to initialize tokio runtime")
	})
}

#[no_mangle]
pub extern "system" fn Java_network_keeta_examples_KeetaNetJNI_accountFromSeed(
	mut env: JNIEnv,
	_class: JClass,
	seed_hex: JString,
	index: jint,
	key_type: jint,
) -> jlong {
	if index < 0 {
		return 0;
	}

	let seed_str: String = match env.get_string(&seed_hex) {
		Ok(s) => s.into(),
		Err(_) => return 0,
	};

	// `Keyable::HexSeed` performs the canonical seed+index key derivation,
	// matching `Account.fromSeed(seed, index)` in the TypeScript SDK.
	let keyable = Keyable::HexSeed((seed_str.into_secret(), index as u32));

	macro_rules! from_seed {
		($key:ty, $kind:ident, $variant:ident) => {
			Account::<$key>::try_from(Accountable::KeyAndType(keyable, KeyPairType::$kind))
				.map(GenericAccount::$variant)
		};
	}

	let account = match key_type {
		t if t == KeyPairType::ECDSASECP256K1 as jint => {
			from_seed!(keetanetwork_account::KeyECDSASECP256K1, ECDSASECP256K1, EcdsaSecp256k1)
		}
		t if t == KeyPairType::ED25519 as jint => {
			from_seed!(keetanetwork_account::KeyED25519, ED25519, Ed25519)
		}
		t if t == KeyPairType::ECDSASECP256R1 as jint => {
			from_seed!(keetanetwork_account::KeyECDSASECP256R1, ECDSASECP256R1, EcdsaSecp256r1)
		}
		_ => return 0, // Not a cryptographic account type
	};

	match account {
		Ok(account) => account_to_handle(account),
		Err(_) => 0,
	}
}

/// Construct an account from a `keeta_`-prefixed public key string.
/// The string encodes the key type and public key data.
#[no_mangle]
pub extern "system" fn Java_network_keeta_examples_KeetaNetJNI_accountFromPublicKeyString(
	mut env: JNIEnv,
	_class: JClass,
	public_key_string: JString,
) -> jlong {
	let key_str: String = match env.get_string(&public_key_string) {
		Ok(s) => s.into(),
		Err(_) => return 0,
	};

	match GenericAccount::from_str(&key_str) {
		Ok(account) => account_to_handle(account),
		Err(err) => {
			eprintln!("{err:?}");
			0
		}
	}
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

	let account = unsafe { account_ref(account_ptr) };
	// Display renders the `keeta_`-prefixed address
	match env.new_string(account.to_string()) {
		Ok(jstr) => jstr.into_raw(),
		Err(_) => ptr::null_mut(),
	}
}

#[no_mangle]
pub extern "system" fn Java_network_keeta_examples_KeetaNetJNI_getAccountType(
	_env: JNIEnv,
	_class: JClass,
	account_ptr: jlong,
) -> jint {
	if account_ptr == 0 {
		return -1;
	}

	unsafe { account_ref(account_ptr) }.to_keypair_type() as jint
}

/// Get the account's raw public key with its type prefix byte
/// (mirrors `account.publicKeyAndType` in the TypeScript SDK).
#[no_mangle]
pub extern "system" fn Java_network_keeta_examples_KeetaNetJNI_getAccountPublicKeyAndType(
	env: JNIEnv,
	_class: JClass,
	account_ptr: jlong,
) -> jbyteArray {
	if account_ptr == 0 {
		return ptr::null_mut();
	}

	let bytes = unsafe { account_ref(account_ptr) }.to_public_key_with_type();
	match env.byte_array_from_slice(&bytes) {
		Ok(arr) => arr.into_raw(),
		Err(_) => ptr::null_mut(),
	}
}

#[no_mangle]
pub extern "system" fn Java_network_keeta_examples_KeetaNetJNI_accountHasPrivateKey(
	_env: JNIEnv,
	_class: JClass,
	account_ptr: jlong,
) -> jboolean {
	if account_ptr == 0 {
		return 0;
	}

	// Identifier accounts (NETWORK/TOKEN/STORAGE/MULTISIG) never carry keys
	let has_private_key = match unsafe { account_ref(account_ptr) }.as_ref() {
		GenericAccount::EcdsaSecp256k1(account) => account.has_private_key(),
		GenericAccount::EcdsaSecp256r1(account) => account.has_private_key(),
		GenericAccount::Ed25519(account) => account.has_private_key(),
		_ => false,
	};
	has_private_key as jboolean
}

#[no_mangle]
pub extern "system" fn Java_network_keeta_examples_KeetaNetJNI_accountIsIdentifier(
	_env: JNIEnv,
	_class: JClass,
	account_ptr: jlong,
) -> jboolean {
	if account_ptr == 0 {
		return 0;
	}

	unsafe { account_ref(account_ptr) }.to_keypair_type().is_identifier() as jboolean
}

/// Derive an identifier account (NETWORK/TOKEN/STORAGE/MULTISIG) relative to
/// this account, a previous block hash (or null for the account opening hash),
/// and an operation index. Mirrors `account.generateIdentifier(...)` in the
/// TypeScript SDK.
#[no_mangle]
pub extern "system" fn Java_network_keeta_examples_KeetaNetJNI_generateIdentifier(
	env: JNIEnv,
	_class: JClass,
	account_ptr: jlong,
	identifier_type: jint,
	block_hash: JByteArray,
	operation_index: jint,
) -> jlong {
	if account_ptr == 0 {
		return 0;
	}
	if operation_index < 0 {
		return 0;
	}

	let identifier_type = match identifier_type {
		t if t == KeyPairType::NETWORK as jint => KeyPairType::NETWORK,
		t if t == KeyPairType::TOKEN as jint => KeyPairType::TOKEN,
		t if t == KeyPairType::STORAGE as jint => KeyPairType::STORAGE,
		t if t == KeyPairType::MULTISIG as jint => KeyPairType::MULTISIG,
		_ => return 0, // Not an identifier key type
	};

	// A null block hash derives against the account opening hash
	let hash: Option<BlockHash> = if block_hash.is_null() {
		None
	} else {
		let bytes: Option<[u8; 32]> = env
			.convert_byte_array(&block_hash)
			.ok()
			.and_then(|bytes| bytes.try_into().ok());
		match bytes {
			Some(bytes) => Some(BlockHash::from(bytes)),
			None => return 0,
		}
	};

	let account = unsafe { account_ref(account_ptr) };
	match account.generate_identifier(identifier_type, hash.as_ref(), operation_index as u32) {
		Ok(identifier) => account_to_handle(identifier),
		Err(err) => {
			eprintln!("{err:?}");
			0
		}
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

	let account = unsafe { account_ref(account_ptr) };
	let message_bytes = match env.convert_byte_array(&message) {
		Ok(bytes) => bytes,
		Err(_) => return JByteArray::default(),
	};

	match account.sign(&message_bytes, None) {
		Ok(sig) => env.byte_array_from_slice(&sig).unwrap_or_default(),
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

	let account = unsafe { account_ref(account_ptr) };
	let message_bytes = match env.convert_byte_array(&message) {
		Ok(bytes) => bytes,
		Err(_) => return 0,
	};
	let signature_bytes = match env.convert_byte_array(&signature) {
		Ok(bytes) => bytes,
		Err(_) => return 0,
	};

	// `AccountVerifier` is implemented per `Account<K>`, not on
	// `GenericAccount`, so dispatch over the cryptographic variants.
	let result = match account.as_ref() {
		GenericAccount::Ed25519(acc) => acc.verify(&message_bytes, &signature_bytes, None),
		GenericAccount::EcdsaSecp256k1(acc) => acc.verify(&message_bytes, &signature_bytes, None),
		GenericAccount::EcdsaSecp256r1(acc) => acc.verify(&message_bytes, &signature_bytes, None),
		_ => return 0, // Identifier accounts cannot verify
	};

	match result {
		Ok(_) => 1,
		Err(_) => 0,
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
			let _ = Box::from_raw(account_ptr as *mut AccountRef);
		}
	}
}

// ---------------------------------------------------------------------------
// User client (network transmission)
// ---------------------------------------------------------------------------

fn derive_base_token(network: Network) -> Option<AccountRef> {
	let id = u64::try_from(network.id()).ok()?;
	let network_account = Account::<KeyNETWORK>::generate_network_address(id).ok()?;
	let token = network_account.generate_identifier(KeyPairType::TOKEN, None, 0).ok()?;
	Some(Arc::new(token))
}

#[no_mangle]
pub extern "system" fn Java_network_keeta_examples_KeetaNetJNI_userClientFromNetwork(
	mut env: JNIEnv,
	_class: JClass,
	network_name: JString,
	signer_ptr: jlong,
) -> jlong {
	let network_name: String = match env.get_string(&network_name) {
		Ok(s) => s.into(),
		Err(_) => return 0,
	};
	let network = match Network::from_str(&network_name) {
		Ok(network) => network,
		Err(err) => {
			eprintln!("{err:?}");
			return 0;
		}
	};

	let signer = if signer_ptr == 0 {
		None
	} else {
		Some(unsafe { account_ref(signer_ptr) }.clone())
	};

	let client = match UserClient::from_network(network, signer) {
		Ok(client) => client,
		Err(err) => {
			eprintln!("{err:?}");
			return 0;
		}
	};

	Box::into_raw(Box::new(UserClientHandle { client, network })) as jlong
}

#[no_mangle]
pub extern "system" fn Java_network_keeta_examples_KeetaNetJNI_userClientGetBaseToken(
	_env: JNIEnv,
	_class: JClass,
	client_ptr: jlong,
) -> jlong {
	if client_ptr == 0 {
		return 0;
	}
	let handle = unsafe { user_client_ref(client_ptr) };
	match derive_base_token(handle.network) {
		Some(token) => account_ref_to_handle(token),
		None => 0,
	}
}

#[no_mangle]
pub extern "system" fn Java_network_keeta_examples_KeetaNetJNI_userClientGetBalance(
	env: JNIEnv,
	_class: JClass,
	client_ptr: jlong,
	account_ptr: jlong,
	token_ptr: jlong,
) -> jstring {
	if client_ptr == 0 || account_ptr == 0 || token_ptr == 0 {
		return ptr::null_mut();
	}

	let handle = unsafe { user_client_ref(client_ptr) };
	let account = unsafe { account_ref(account_ptr) };
	let token = unsafe { account_ref(token_ptr) };

	let result = runtime().block_on(async {
		handle
			.client
			.client()
			.balance(account.to_string(), token.to_string())
			.await
	});

	match result {
		Ok(balance) => match env.new_string(balance.to_string()) {
			Ok(jstr) => jstr.into_raw(),
			Err(_) => ptr::null_mut(),
		},
		Err(err) => {
			eprintln!("{err:?}");
			ptr::null_mut()
		}
	}
}

#[no_mangle]
pub extern "system" fn Java_network_keeta_examples_KeetaNetJNI_userClientHead<'local>(
	env: JNIEnv<'local>,
	_class: JClass,
	client_ptr: jlong,
) -> JByteArray<'local> {
	if client_ptr == 0 {
		return JByteArray::default();
	}

	let handle = unsafe { user_client_ref(client_ptr) };
	let result = runtime().block_on(async { handle.client.head().await });
	match result {
		Ok(Some(block)) => env.byte_array_from_slice(block.hash().as_bytes()).unwrap_or_default(),
		Ok(None) => JByteArray::default(),
		Err(err) => {
			eprintln!("{err:?}");
			JByteArray::default()
		}
	}
}

#[no_mangle]
pub extern "system" fn Java_network_keeta_examples_KeetaNetJNI_userClientHeadForAccount<'local>(
	env: JNIEnv<'local>,
	_class: JClass,
	client_ptr: jlong,
	account_ptr: jlong,
) -> JByteArray<'local> {
	if client_ptr == 0 || account_ptr == 0 {
		return JByteArray::default();
	}

	let handle = unsafe { user_client_ref(client_ptr) };
	let account = unsafe { account_ref(account_ptr) };
	let result = runtime().block_on(async { handle.client.client().head_block(account.to_string()).await });

	match result {
		Ok(Some(block)) => env.byte_array_from_slice(block.hash().as_bytes()).unwrap_or_default(),
		Ok(None) => JByteArray::default(),
		Err(err) => {
			eprintln!("{err:?}");
			JByteArray::default()
		}
	}
}

#[no_mangle]
pub extern "system" fn Java_network_keeta_examples_KeetaNetJNI_userClientTransmit(
	env: JNIEnv,
	_class: JClass,
	client_ptr: jlong,
	block_ptrs: JLongArray,
) -> jboolean {
	if client_ptr == 0 {
		return 0;
	}

	let count = match env.get_array_length(&block_ptrs) {
		Ok(v) => v as usize,
		Err(_) => return 0,
	};

	let mut ptrs = vec![0i64; count];
	if env.get_long_array_region(&block_ptrs, 0, &mut ptrs).is_err() {
		return 0;
	}

	let mut blocks = Vec::with_capacity(count);
	for ptr in ptrs {
		if ptr == 0 {
			return 0;
		}
		blocks.push(unsafe { &*(ptr as *const Block) }.clone());
	}

	let handle = unsafe { user_client_ref(client_ptr) };
	match runtime().block_on(async { handle.client.transmit(&blocks, TransmitOptions::default()).await }) {
		Ok(ok) => ok as jboolean,
		Err(err) => {
			eprintln!("{err:?}");
			0
		}
	}
}

#[no_mangle]
pub extern "system" fn Java_network_keeta_examples_KeetaNetJNI_userClientGenerateIdentifier(
	_env: JNIEnv,
	_class: JClass,
	client_ptr: jlong,
	key_type: jint,
) -> jlong {
	if client_ptr == 0 {
		return 0;
	}

	let key_type = match key_type {
		t if t == KeyPairType::NETWORK as jint => KeyPairType::NETWORK,
		t if t == KeyPairType::TOKEN as jint => KeyPairType::TOKEN,
		t if t == KeyPairType::STORAGE as jint => KeyPairType::STORAGE,
		t if t == KeyPairType::MULTISIG as jint => KeyPairType::MULTISIG,
		_ => return 0,
	};

	let handle = unsafe { user_client_ref(client_ptr) };
	match runtime().block_on(async { handle.client.generate_identifier(key_type, None).await }) {
		Ok(identifier) => account_ref_to_handle(identifier),
		Err(err) => {
			eprintln!("{err:?}");
			0
		}
	}
}

#[no_mangle]
pub extern "system" fn Java_network_keeta_examples_KeetaNetJNI_userClientUpdatePermissions(
	_env: JNIEnv,
	_class: JClass,
	client_ptr: jlong,
	principal_ptr: jlong,
	permissions_bits: jlong,
	adjust_method: jint,
	target_ptr: jlong,
) -> jboolean {
	if client_ptr == 0 || principal_ptr == 0 {
		return 0;
	}

	let method = match adjust_method {
		0 => AdjustMethod::Add,
		1 => AdjustMethod::Subtract,
		2 => AdjustMethod::Set,
		_ => return 0,
	};
	let permissions = match permissions_from_bits(permissions_bits) {
		Ok(permissions) => permissions,
		Err(err) => {
			eprintln!("{err:?}");
			return 0;
		}
	};

	let principal = unsafe { account_ref(principal_ptr) };
	let target = if target_ptr == 0 {
		None
	} else {
		Some(unsafe { account_ref(target_ptr) }.clone())
	};
	let payload = ModifyPermissions {
		principal: ModifyPermissionsPrincipal::Account(principal.clone()),
		method,
		permissions: Some(permissions),
		target,
	};

	let handle = unsafe { user_client_ref(client_ptr) };
	match runtime().block_on(async { handle.client.update_permissions(payload).await }) {
		Ok(ok) => ok as jboolean,
		Err(err) => {
			eprintln!("{err:?}");
			0
		}
	}
}

#[no_mangle]
pub extern "system" fn Java_network_keeta_examples_KeetaNetJNI_freeUserClient(
	_env: JNIEnv,
	_class: JClass,
	client_ptr: jlong,
) {
	if client_ptr != 0 {
		unsafe {
			let _ = Box::from_raw(client_ptr as *mut UserClientHandle);
		}
	}
}

// ---------------------------------------------------------------------------
// Operations
// ---------------------------------------------------------------------------

fn permissions_from_bits(bits: jlong) -> Result<Permissions, keetanetwork_block::BlockError> {
	Permissions::from_bigints(BigInt::from(bits as u64), BigInt::ZERO)
}

/// Build a CREATE_IDENTIFIER operation for a 3-signer multisig.
/// Returns an `Operation` handle.
#[no_mangle]
pub extern "system" fn Java_network_keeta_examples_KeetaNetJNI_createMultisigOperation(
	_env: JNIEnv,
	_class: JClass,
	multisig_ptr: jlong,
	signer1_ptr: jlong,
	signer2_ptr: jlong,
	signer3_ptr: jlong,
	quorum: jint,
) -> jlong {
	if multisig_ptr == 0 || signer1_ptr == 0 || signer2_ptr == 0 || signer3_ptr == 0 {
		return 0;
	}
	if !(1..=3).contains(&quorum) {
		return 0;
	}

	let multisig = unsafe { account_ref(multisig_ptr) };
	let signers: Vec<AccountRef> = [signer1_ptr, signer2_ptr, signer3_ptr]
		.iter()
		.map(|ptr| unsafe { account_ref(*ptr) }.clone())
		.collect();

	let operation: Operation = CreateIdentifier {
		identifier: multisig.clone(),
		create_arguments: Some(IdentifierCreateArguments::Multisig(MultisigCreateArguments {
			signers,
			quorum: BigInt::from(quorum),
		})),
	}
	.into();

	Box::into_raw(Box::new(operation)) as jlong
}

/// Build a MODIFY_PERMISSIONS operation. Returns an `Operation` handle.
#[no_mangle]
pub extern "system" fn Java_network_keeta_examples_KeetaNetJNI_createModifyPermissionsOperation(
	_env: JNIEnv,
	_class: JClass,
	principal_ptr: jlong,
	permissions_bits: jlong,
	adjust_method: jint,
) -> jlong {
	if principal_ptr == 0 {
		return 0;
	}

	let principal = unsafe { account_ref(principal_ptr) };

	let method = match adjust_method {
		0 => AdjustMethod::Add,
		1 => AdjustMethod::Subtract,
		2 => AdjustMethod::Set,
		_ => return 0,
	};

	let permissions = match permissions_from_bits(permissions_bits) {
		Ok(permissions) => permissions,
		Err(_) => return 0,
	};

	let operation: Operation = ModifyPermissions {
		principal: ModifyPermissionsPrincipal::Account(principal.clone()),
		method,
		permissions: Some(permissions),
		target: None, // Applies to the block's account
	}
	.into();

	Box::into_raw(Box::new(operation)) as jlong
}

/// Build a SET_INFO operation. Returns an `Operation` handle.
#[no_mangle]
pub extern "system" fn Java_network_keeta_examples_KeetaNetJNI_createSetInfoOperation(
	mut env: JNIEnv,
	_class: JClass,
	name: JString,
	description: JString,
	metadata: JString,
	default_permission_bits: jlong,
) -> jlong {
	let mut get = |s: &JString| -> Option<String> { env.get_string(s).ok().map(Into::into) };
	let (Some(name), Some(description), Some(metadata)) = (get(&name), get(&description), get(&metadata)) else {
		return 0;
	};

	let default_permission = if default_permission_bits != 0 {
		match permissions_from_bits(default_permission_bits) {
			Ok(permissions) => Some(permissions),
			Err(_) => return 0,
		}
	} else {
		None
	};

	let operation: Operation = SetInfo { name, description, metadata, default_permission }.into();

	Box::into_raw(Box::new(operation)) as jlong
}

#[no_mangle]
pub extern "system" fn Java_network_keeta_examples_KeetaNetJNI_freeOperation(
	_env: JNIEnv,
	_class: JClass,
	operation_ptr: jlong,
) {
	if operation_ptr != 0 {
		unsafe {
			let _ = Box::from_raw(operation_ptr as *mut Operation);
		}
	}
}

// ---------------------------------------------------------------------------
// Block builder
// ---------------------------------------------------------------------------

/// Apply a consuming `BlockBuilder` method to a raw handle, returning a new
/// handle (the old handle is always consumed).
fn rebox_builder(builder_ptr: jlong, apply: impl FnOnce(BlockBuilder) -> Option<BlockBuilder>) -> jlong {
	if builder_ptr == 0 {
		return 0;
	}

	let builder = unsafe { Box::from_raw(builder_ptr as *mut BlockBuilder) };
	match apply(*builder) {
		Some(builder) => Box::into_raw(Box::new(builder)) as jlong,
		None => 0,
	}
}

#[no_mangle]
pub extern "system" fn Java_network_keeta_examples_KeetaNetJNI_createBlockBuilder(
	_env: JNIEnv,
	_class: JClass,
) -> jlong {
	Box::into_raw(Box::new(BlockBuilder::default())) as jlong
}

#[no_mangle]
pub extern "system" fn Java_network_keeta_examples_KeetaNetJNI_blockBuilderSetVersion(
	_env: JNIEnv,
	_class: JClass,
	builder_ptr: jlong,
	version: jint,
) -> jlong {
	rebox_builder(builder_ptr, |builder| {
		let version = match version {
			1 => BlockVersion::V1,
			2 => BlockVersion::V2,
			_ => return None,
		};
		Some(builder.with_version(version))
	})
}

#[no_mangle]
pub extern "system" fn Java_network_keeta_examples_KeetaNetJNI_blockBuilderSetNetwork(
	_env: JNIEnv,
	_class: JClass,
	builder_ptr: jlong,
	network: jlong,
) -> jlong {
	rebox_builder(builder_ptr, |builder| Some(builder.with_network(network as u64)))
}

#[no_mangle]
pub extern "system" fn Java_network_keeta_examples_KeetaNetJNI_blockBuilderSetAccount(
	_env: JNIEnv,
	_class: JClass,
	builder_ptr: jlong,
	account_ptr: jlong,
) -> jlong {
	if account_ptr == 0 {
		return rebox_builder(builder_ptr, |_| None);
	}
	let account = unsafe { account_ref(account_ptr) }.clone();
	rebox_builder(builder_ptr, |builder| Some(builder.with_account(account)))
}

#[no_mangle]
pub extern "system" fn Java_network_keeta_examples_KeetaNetJNI_blockBuilderSetSigner(
	_env: JNIEnv,
	_class: JClass,
	builder_ptr: jlong,
	signer_ptr: jlong,
) -> jlong {
	if signer_ptr == 0 {
		return rebox_builder(builder_ptr, |_| None);
	}
	let signer = unsafe { account_ref(signer_ptr) }.clone();
	rebox_builder(builder_ptr, |builder| Some(builder.with_signer(Signer::Single(signer))))
}

/// Set a multisig signer: the multisig address plus the member accounts
/// actually producing signatures (which may be a quorum subset).
#[no_mangle]
pub extern "system" fn Java_network_keeta_examples_KeetaNetJNI_blockBuilderSetMultisigSigner(
	env: JNIEnv,
	_class: JClass,
	builder_ptr: jlong,
	multisig_ptr: jlong,
	signer_ptrs: JLongArray,
) -> jlong {
	let signers: Option<Vec<Signer>> = (|| {
		if multisig_ptr == 0 {
			return None;
		}
		let count = env.get_array_length(&signer_ptrs).ok()? as usize;
		let mut ptrs = vec![0i64; count];
		env.get_long_array_region(&signer_ptrs, 0, &mut ptrs).ok()?;

		let mut signers = Vec::with_capacity(count);
		for ptr in ptrs {
			if ptr == 0 {
				return None;
			}
			signers.push(Signer::Single(unsafe { account_ref(ptr) }.clone()));
		}
		Some(signers)
	})();

	rebox_builder(builder_ptr, |builder| {
		let signers = signers?;
		let address = unsafe { account_ref(multisig_ptr) }.clone();
		Some(builder.with_signer(Signer::Multisig { address, signers }))
	})
}

#[no_mangle]
pub extern "system" fn Java_network_keeta_examples_KeetaNetJNI_blockBuilderSetPrevious(
	env: JNIEnv,
	_class: JClass,
	builder_ptr: jlong,
	previous_hash: JByteArray,
) -> jlong {
	let hash: Option<[u8; 32]> = env
		.convert_byte_array(&previous_hash)
		.ok()
		.and_then(|bytes| bytes.try_into().ok());

	rebox_builder(builder_ptr, |builder| Some(builder.with_previous(BlockHash::from(hash?))))
}

/// Mark the block as the account opening block (no previous hash).
#[no_mangle]
pub extern "system" fn Java_network_keeta_examples_KeetaNetJNI_blockBuilderSetNoPrevious(
	_env: JNIEnv,
	_class: JClass,
	builder_ptr: jlong,
) -> jlong {
	rebox_builder(builder_ptr, |builder| Some(builder.as_opening()))
}

#[no_mangle]
pub extern "system" fn Java_network_keeta_examples_KeetaNetJNI_blockBuilderAddOperation(
	_env: JNIEnv,
	_class: JClass,
	builder_ptr: jlong,
	operation_ptr: jlong,
) -> jlong {
	rebox_builder(builder_ptr, |builder| {
		if operation_ptr == 0 {
			return None;
		}
		let operation = unsafe { &*(operation_ptr as *const Operation) };
		Some(builder.with_operation(operation.clone()))
	})
}

/// Build and validate the unsigned block, consuming the builder.
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
		Err(err) => {
			eprintln!("BlockBuilder::build failed: {err:?}");
			0
		}
	}
}

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

// ---------------------------------------------------------------------------
// Unsigned blocks
// ---------------------------------------------------------------------------

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
	env.byte_array_from_slice(unsigned.hash().as_bytes()).unwrap_or_default()
}

#[no_mangle]
pub extern "system" fn Java_network_keeta_examples_KeetaNetJNI_unsignedBlockGetHashString(
	env: JNIEnv,
	_class: JClass,
	unsigned_ptr: jlong,
) -> jstring {
	if unsigned_ptr == 0 {
		return ptr::null_mut();
	}

	let unsigned = unsafe { &*(unsigned_ptr as *const UnsignedBlock) };
	match env.new_string(unsigned.hash().to_string()) {
		Ok(jstr) => jstr.into_raw(),
		Err(_) => ptr::null_mut(),
	}
}

/// Returns required signer public keys (with key-type prefix) serialized as:
/// count (u32 BE) + (length (u32 BE) + pubkey bytes) * count
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
	let signers = unsigned.required_signers();

	let mut result = Vec::new();
	result.extend_from_slice(&(signers.len() as u32).to_be_bytes());
	for signer in signers {
		let pubkey = signer.to_public_key_with_type();
		result.extend_from_slice(&(pubkey.len() as u32).to_be_bytes());
		result.extend_from_slice(&pubkey);
	}

	env.byte_array_from_slice(&result).unwrap_or_default()
}

/// Sign the block with the private keys held by its required signer
/// accounts and seal it. Consumes the unsigned block handle (even on
/// failure) and returns a `Block` handle.
#[no_mangle]
pub extern "system" fn Java_network_keeta_examples_KeetaNetJNI_unsignedBlockSign(
	_env: JNIEnv,
	_class: JClass,
	unsigned_ptr: jlong,
) -> jlong {
	if unsigned_ptr == 0 {
		return 0;
	}

	let unsigned = unsafe { Box::from_raw(unsigned_ptr as *mut UnsignedBlock) };
	match unsigned.sign() {
		Ok(block) => Box::into_raw(Box::new(block)) as jlong,
		Err(err) => {
			eprintln!("UnsignedBlock::sign failed: {err:?}");
			0
		}
	}
}

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

// ---------------------------------------------------------------------------
// Signed (sealed) blocks
// ---------------------------------------------------------------------------

#[no_mangle]
pub extern "system" fn Java_network_keeta_examples_KeetaNetJNI_signedBlockGetHash<'local>(
	env: JNIEnv<'local>,
	_class: JClass,
	signed_ptr: jlong,
) -> JByteArray<'local> {
	if signed_ptr == 0 {
		return JByteArray::default();
	}

	let block = unsafe { &*(signed_ptr as *const Block) };
	env.byte_array_from_slice(block.hash().as_bytes()).unwrap_or_default()
}

#[no_mangle]
pub extern "system" fn Java_network_keeta_examples_KeetaNetJNI_signedBlockGetHashString(
	env: JNIEnv,
	_class: JClass,
	signed_ptr: jlong,
) -> jstring {
	if signed_ptr == 0 {
		return ptr::null_mut();
	}

	let block = unsafe { &*(signed_ptr as *const Block) };
	match env.new_string(block.hash().to_string()) {
		Ok(jstr) => jstr.into_raw(),
		Err(_) => ptr::null_mut(),
	}
}

/// The signed ASN.1/DER bytes as transmitted on the network.
#[no_mangle]
pub extern "system" fn Java_network_keeta_examples_KeetaNetJNI_signedBlockToBytes<'local>(
	env: JNIEnv<'local>,
	_class: JClass,
	signed_ptr: jlong,
) -> JByteArray<'local> {
	if signed_ptr == 0 {
		return JByteArray::default();
	}

	let block = unsafe { &*(signed_ptr as *const Block) };
	env.byte_array_from_slice(block.to_bytes()).unwrap_or_default()
}

#[no_mangle]
pub extern "system" fn Java_network_keeta_examples_KeetaNetJNI_freeSignedBlock(
	_env: JNIEnv,
	_class: JClass,
	signed_ptr: jlong,
) {
	if signed_ptr != 0 {
		unsafe {
			let _ = Box::from_raw(signed_ptr as *mut Block);
		}
	}
}
