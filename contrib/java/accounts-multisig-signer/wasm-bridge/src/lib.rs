use std::collections::HashMap;
use std::slice;
use std::str::FromStr;
use std::sync::atomic::{AtomicU64, Ordering};
use std::sync::{Arc, Mutex};

use hex::ToHex;
use num_bigint::BigInt;
use once_cell::sync::Lazy;

use keetanetwork_account::account::AccountSigner;
use keetanetwork_account::{Account, Accountable, GenericAccount, KeyNETWORK, KeyPairType, Keyable};
use keetanetwork_block::{
	AccountRef, AdjustMethod, Amount, Block, BlockBuilder, BlockHash, BlockPurpose, BlockVersion, CreateIdentifier, Hashable,
	IdentifierCreateArguments, ModifyPermissions, ModifyPermissionsPrincipal, MultisigCreateArguments, Operation,
	Permissions, Send, SetInfo, Signer, UnsignedBlock, BaseFlag,
};
use keetanetwork_crypto::prelude::*;
use keetanetwork_vote::{Vote, VoteStapleBuilder};

#[derive(Default)]
struct State {
	accounts: HashMap<u64, AccountRef>,
	operations: HashMap<u64, Operation>,
	builders: HashMap<u64, BlockBuilder>,
	unsigned_blocks: HashMap<u64, UnsignedBlock>,
	signed_blocks: HashMap<u64, Block>,
	last_error: Option<String>,
}

static STATE: Lazy<Mutex<State>> = Lazy::new(|| Mutex::new(State::default()));
static NEXT_ID: AtomicU64 = AtomicU64::new(1);

fn with_state<R>(f: impl FnOnce(&mut State) -> R) -> R {
	let mut guard = STATE.lock().expect("state mutex poisoned");
	f(&mut guard)
}

fn next_id() -> u64 {
	NEXT_ID.fetch_add(1, Ordering::Relaxed)
}

fn set_error(err: impl ToString) {
	with_state(|s| {
		s.last_error = Some(err.to_string());
	});
}

fn clear_error() {
	with_state(|s| {
		s.last_error = None;
	});
}

fn pack_ptr_len(ptr: u32, len: u32) -> u64 {
	((ptr as u64) << 32) | (len as u64)
}

fn leak_bytes(bytes: Vec<u8>) -> u64 {
	let len = bytes.len() as u32;
	let boxed = bytes.into_boxed_slice();
	let ptr = boxed.as_ptr() as u32;
	std::mem::forget(boxed);
	pack_ptr_len(ptr, len)
}

fn key_type_from_i32(value: i32) -> Option<KeyPairType> {
	match value {
		v if v == KeyPairType::ECDSASECP256K1 as i32 => Some(KeyPairType::ECDSASECP256K1),
		v if v == KeyPairType::ED25519 as i32 => Some(KeyPairType::ED25519),
		v if v == KeyPairType::NETWORK as i32 => Some(KeyPairType::NETWORK),
		v if v == KeyPairType::TOKEN as i32 => Some(KeyPairType::TOKEN),
		v if v == KeyPairType::STORAGE as i32 => Some(KeyPairType::STORAGE),
		v if v == KeyPairType::ECDSASECP256R1 as i32 => Some(KeyPairType::ECDSASECP256R1),
		v if v == KeyPairType::MULTISIG as i32 => Some(KeyPairType::MULTISIG),
		_ => None,
	}
}

fn identifier_type_from_i32(value: i32) -> Option<KeyPairType> {
	match key_type_from_i32(value) {
		Some(key_type) if key_type.is_identifier() => Some(key_type),
		_ => None,
	}
}

fn permissions_from_bits(bits: i64) -> Result<Permissions, keetanetwork_block::BlockError> {
	Permissions::from_bigints(BigInt::from(bits as u64), BigInt::ZERO)
}

fn leak_i64_array(values: &[i64]) -> u64 {
	let mut bytes = Vec::with_capacity(values.len() * 8);
	for value in values {
		bytes.extend_from_slice(&value.to_le_bytes());
	}
	leak_bytes(bytes)
}

fn read_vec(ptr: u32, len: u32) -> Option<Vec<u8>> {
	if len == 0 {
		return Some(Vec::new());
	}
	if ptr == 0 {
		return None;
	}
	// SAFETY: caller provides a wasm-memory pointer/length region. We only read
	// exactly `len` bytes and immediately copy into an owned Vec.
	let slice = unsafe { slice::from_raw_parts(ptr as *const u8, len as usize) };
	Some(slice.to_vec())
}

fn read_utf8(ptr: u32, len: u32) -> Option<String> {
	let bytes = read_vec(ptr, len)?;
	String::from_utf8(bytes).ok()
}

fn read_u64_vec(ptr: u32, count: u32) -> Option<Vec<u64>> {
	if count == 0 {
		return Some(Vec::new());
	}
	if ptr == 0 {
		return None;
	}
	let total_len = count as usize * 8;
	// SAFETY: caller supplies a contiguous u64 array in wasm memory.
	let bytes = unsafe { slice::from_raw_parts(ptr as *const u8, total_len) };
	let mut out = Vec::with_capacity(count as usize);
	for chunk in bytes.chunks_exact(8) {
		let value = u64::from_le_bytes(chunk.try_into().ok()?);
		out.push(value);
	}
	Some(out)
}

fn read_ptr_len_array(ptr: u32, count: u32) -> Option<Vec<(u32, u32)>> {
	if count == 0 {
		return Some(Vec::new());
	}
	if ptr == 0 {
		return None;
	}
	let total_len = count as usize * 8;
	// SAFETY: caller supplies a contiguous array of u32 ptr/len pairs.
	let bytes = unsafe { slice::from_raw_parts(ptr as *const u8, total_len) };
	let mut out = Vec::with_capacity(count as usize);
	for chunk in bytes.chunks_exact(8) {
		let p = u32::from_le_bytes(chunk[0..4].try_into().ok()?);
		let l = u32::from_le_bytes(chunk[4..8].try_into().ok()?);
		out.push((p, l));
	}
	Some(out)
}

#[no_mangle]
pub extern "C" fn kn_alloc(len: u32) -> u32 {
	if len == 0 {
		return 0;
	}
	let boxed = vec![0u8; len as usize].into_boxed_slice();
	let ptr = boxed.as_ptr() as u32;
	std::mem::forget(boxed);
	ptr
}

#[no_mangle]
pub extern "C" fn kn_free(ptr: u32, len: u32) {
	if ptr == 0 || len == 0 {
		return;
	}
	// SAFETY: ptr/len must originate from `kn_alloc`/`leak_bytes` in this module.
	unsafe {
		let data = slice::from_raw_parts_mut(ptr as *mut u8, len as usize);
		let _ = Box::from_raw(data as *mut [u8]);
	}
}

#[no_mangle]
pub extern "C" fn kn_last_error() -> u64 {
	let msg = with_state(|s| s.last_error.take().unwrap_or_default());
	leak_bytes(msg.into_bytes())
}

#[no_mangle]
pub extern "C" fn kn_generate_random_seed() -> u64 {
	clear_error();
	match keetanetwork_crypto::utils::generate_random_seed() {
		Ok(seed) => leak_bytes(hex::encode(seed.expose_secret()).into_bytes()),
		Err(err) => {
			set_error(err);
			0
		}
	}
}

#[no_mangle]
pub extern "C" fn kn_get_account_type_constants() -> u64 {
	let values: [i64; 7] = [
		KeyPairType::ECDSASECP256K1 as i64,
		KeyPairType::ED25519 as i64,
		KeyPairType::NETWORK as i64,
		KeyPairType::TOKEN as i64,
		KeyPairType::STORAGE as i64,
		KeyPairType::ECDSASECP256R1 as i64,
		KeyPairType::MULTISIG as i64,
	];
	leak_i64_array(&values)
}

#[no_mangle]
pub extern "C" fn kn_get_permission_constants() -> u64 {
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
	leak_i64_array(&values)
}

#[no_mangle]
pub extern "C" fn kn_account_from_seed(seed_ptr: u32, seed_len: u32, index: u32, key_type: i32) -> u64 {
	clear_error();
	let Some(seed_hex) = read_utf8(seed_ptr, seed_len) else {
		set_error("invalid seed utf-8");
		return 0;
	};

	let key_type = match key_type_from_i32(key_type) {
		Some(key_type) => key_type,
		None => {
			set_error("unsupported key type");
			return 0;
		}
	};

	let keyable = Keyable::HexSeed((seed_hex.into_secret(), index));
	let account = match key_type {
		KeyPairType::ECDSASECP256K1 => Account::<keetanetwork_account::KeyECDSASECP256K1>::try_from(
			Accountable::KeyAndType(keyable, key_type),
		)
		.map(GenericAccount::EcdsaSecp256k1),
		KeyPairType::ED25519 => {
			Account::<keetanetwork_account::KeyED25519>::try_from(Accountable::KeyAndType(keyable, key_type))
				.map(GenericAccount::Ed25519)
		}
		KeyPairType::ECDSASECP256R1 => Account::<keetanetwork_account::KeyECDSASECP256R1>::try_from(
			Accountable::KeyAndType(keyable, key_type),
		)
		.map(GenericAccount::EcdsaSecp256r1),
		_ => {
			set_error("seed derivation only supported for cryptographic key types");
			return 0;
		}
	};

	match account {
		Ok(account) => {
			let id = next_id();
			with_state(|s| {
				s.accounts.insert(id, Arc::new(account));
			});
			id
		}
		Err(err) => {
			set_error(err);
			0
		}
	}
}

#[no_mangle]
pub extern "C" fn kn_account_from_public_key_string(ptr: u32, len: u32) -> u64 {
	clear_error();
	let Some(value) = read_utf8(ptr, len) else {
		set_error("invalid public key utf-8");
		return 0;
	};
	match GenericAccount::from_str(&value) {
		Ok(account) => {
			let id = next_id();
			with_state(|s| {
				s.accounts.insert(id, Arc::new(account));
			});
			id
		}
		Err(err) => {
			set_error(err);
			0
		}
	}
}

#[no_mangle]
pub extern "C" fn kn_get_account_public_key(account_id: u64) -> u64 {
	clear_error();
	with_state(|s| match s.accounts.get(&account_id) {
		Some(account) => leak_bytes(account.to_string().into_bytes()),
		None => {
			s.last_error = Some("invalid account handle".to_owned());
			0
		}
	})
}

#[no_mangle]
pub extern "C" fn kn_get_account_public_key_and_type(account_id: u64) -> u64 {
	clear_error();
	with_state(|s| match s.accounts.get(&account_id) {
		Some(account) => leak_bytes(account.to_public_key_with_type()),
		None => {
			s.last_error = Some("invalid account handle".to_owned());
			0
		}
	})
}

#[no_mangle]
pub extern "C" fn kn_get_account_public_key_and_type_string(account_id: u64) -> u64 {
	clear_error();
	with_state(|s| match s.accounts.get(&account_id) {
		Some(account) => {
			let value = format!("0x{}", account.encode_hex_upper::<String>());
			leak_bytes(value.into_bytes())
		}
		None => {
			s.last_error = Some("invalid account handle".to_owned());
			0
		}
	})
}

#[no_mangle]
pub extern "C" fn kn_account_has_private_key(account_id: u64) -> u32 {
	with_state(|s| match s.accounts.get(&account_id).map(Arc::as_ref) {
		Some(GenericAccount::EcdsaSecp256k1(account)) => account.has_private_key() as u32,
		Some(GenericAccount::EcdsaSecp256r1(account)) => account.has_private_key() as u32,
		Some(GenericAccount::Ed25519(account)) => account.has_private_key() as u32,
		Some(_) => 0,
		None => 0,
	})
}

#[no_mangle]
pub extern "C" fn kn_account_is_identifier(account_id: u64) -> u32 {
	with_state(|s| match s.accounts.get(&account_id) {
		Some(account) => account.to_keypair_type().is_identifier() as u32,
		None => 0,
	})
}

#[no_mangle]
pub extern "C" fn kn_generate_identifier(
	account_id: u64,
	identifier_type: i32,
	block_hash_ptr: u32,
	block_hash_len: u32,
	operation_index: u32,
) -> u64 {
	clear_error();
	let identifier_type = match identifier_type_from_i32(identifier_type) {
		Some(identifier_type) => identifier_type,
		None => {
			set_error("invalid identifier type");
			return 0;
		}
	};

	let hash = if block_hash_len == 0 {
		None
	} else {
		let Some(bytes) = read_vec(block_hash_ptr, block_hash_len) else {
			set_error("invalid block hash pointer");
			return 0;
		};
		let Ok(bytes) = <[u8; 32]>::try_from(bytes.as_slice()) else {
			set_error("block hash must be exactly 32 bytes");
			return 0;
		};
		Some(BlockHash::from(bytes))
	};

	let account = with_state(|s| s.accounts.get(&account_id).cloned());
	let Some(account) = account else {
		set_error("invalid account handle");
		return 0;
	};

	match account.generate_identifier(identifier_type, hash.as_ref(), operation_index) {
		Ok(identifier) => {
			let id = next_id();
			with_state(|s| {
				s.accounts.insert(id, Arc::new(identifier));
			});
			id
		}
		Err(err) => {
			set_error(err);
			0
		}
	}
}

#[no_mangle]
pub extern "C" fn kn_network_base_token(network_id: u64) -> u64 {
	clear_error();
	let network = match Account::<KeyNETWORK>::generate_network_address(network_id) {
		Ok(network) => network,
		Err(err) => {
			set_error(err);
			return 0;
		}
	};
	match network.generate_identifier(KeyPairType::TOKEN, None, 0) {
		Ok(token) => {
			let id = next_id();
			with_state(|s| {
				s.accounts.insert(id, Arc::new(token));
			});
			id
		}
		Err(err) => {
			set_error(err);
			0
		}
	}
}

#[no_mangle]
pub extern "C" fn kn_free_account(account_id: u64) {
	with_state(|s| {
		s.accounts.remove(&account_id);
	});
}

#[no_mangle]
pub extern "C" fn kn_get_account_type(account_id: u64) -> i32 {
	with_state(|s| match s.accounts.get(&account_id) {
		Some(account) => account.to_keypair_type() as i32,
		None => -1,
	})
}

#[no_mangle]
pub extern "C" fn kn_sign_message(account_id: u64, msg_ptr: u32, msg_len: u32) -> u64 {
	clear_error();
	let Some(message) = read_vec(msg_ptr, msg_len) else {
		set_error("invalid message pointer");
		return 0;
	};
	let account = with_state(|s| s.accounts.get(&account_id).cloned());
	let Some(account) = account else {
		set_error("invalid account handle");
		return 0;
	};
	match account.sign(&message, None) {
		Ok(signature) => leak_bytes(signature),
		Err(err) => {
			set_error(err);
			0
		}
	}
}

#[no_mangle]
pub extern "C" fn kn_verify_signature(
	account_id: u64,
	msg_ptr: u32,
	msg_len: u32,
	sig_ptr: u32,
	sig_len: u32,
) -> i32 {
	let Some(message) = read_vec(msg_ptr, msg_len) else {
		return 0;
	};
	let Some(signature) = read_vec(sig_ptr, sig_len) else {
		return 0;
	};
	with_state(|s| {
		let Some(account) = s.accounts.get(&account_id) else {
			return 0;
		};
		let verified = match account.as_ref() {
			GenericAccount::Ed25519(acc) => acc.verify(&message, &signature, None),
			GenericAccount::EcdsaSecp256k1(acc) => acc.verify(&message, &signature, None),
			GenericAccount::EcdsaSecp256r1(acc) => acc.verify(&message, &signature, None),
			_ => return 0,
		};
		if verified.is_ok() { 1 } else { 0 }
	})
}

#[no_mangle]
pub extern "C" fn kn_create_identifier_operation(identifier_id: u64) -> u64 {
	clear_error();
	let identifier = with_state(|s| s.accounts.get(&identifier_id).cloned());
	let Some(identifier) = identifier else {
		set_error("invalid identifier handle");
		return 0;
	};

	let operation: Operation = CreateIdentifier {
		identifier,
		create_arguments: None,
	}
	.into();

	let id = next_id();
	with_state(|s| {
		s.operations.insert(id, operation);
	});
	id
}

#[no_mangle]
pub extern "C" fn kn_create_multisig_operation(
	multisig_id: u64,
	signer1_id: u64,
	signer2_id: u64,
	signer3_id: u64,
	quorum: i32,
) -> u64 {
	clear_error();
	if !(1..=3).contains(&quorum) {
		set_error("quorum must be in range [1, 3]");
		return 0;
	}
	let (multisig, signer1, signer2, signer3) = with_state(|s| {
		(
			s.accounts.get(&multisig_id).cloned(),
			s.accounts.get(&signer1_id).cloned(),
			s.accounts.get(&signer2_id).cloned(),
			s.accounts.get(&signer3_id).cloned(),
		)
	});
	let (Some(multisig), Some(signer1), Some(signer2), Some(signer3)) = (multisig, signer1, signer2, signer3) else {
		set_error("invalid account handle for multisig operation");
		return 0;
	};

	let operation: Operation = CreateIdentifier {
		identifier: multisig,
		create_arguments: Some(IdentifierCreateArguments::Multisig(MultisigCreateArguments {
			signers: vec![signer1, signer2, signer3],
			quorum: BigInt::from(quorum),
		})),
	}
	.into();

	let id = next_id();
	with_state(|s| {
		s.operations.insert(id, operation);
	});
	id
}

#[no_mangle]
pub extern "C" fn kn_create_modify_permissions_operation(
	principal_id: u64,
	permissions_bits: i64,
	adjust_method: i32,
) -> u64 {
	clear_error();
	let method = match adjust_method {
		0 => AdjustMethod::Add,
		1 => AdjustMethod::Subtract,
		2 => AdjustMethod::Set,
		_ => {
			set_error("invalid adjust method");
			return 0;
		}
	};

	let permissions = match permissions_from_bits(permissions_bits) {
		Ok(permissions) => permissions,
		Err(err) => {
			set_error(err);
			return 0;
		}
	};

	let principal = with_state(|s| s.accounts.get(&principal_id).cloned());
	let Some(principal) = principal else {
		set_error("invalid principal handle");
		return 0;
	};

	let operation: Operation = ModifyPermissions {
		principal: ModifyPermissionsPrincipal::Account(principal),
		method,
		permissions: Some(permissions),
		target: None,
	}
	.into();

	let id = next_id();
	with_state(|s| {
		s.operations.insert(id, operation);
	});
	id
}

#[no_mangle]
pub extern "C" fn kn_create_set_info_operation(
	name_ptr: u32,
	name_len: u32,
	desc_ptr: u32,
	desc_len: u32,
	metadata_ptr: u32,
	metadata_len: u32,
	default_permission_bits: i64,
) -> u64 {
	clear_error();
	let (Some(name), Some(description), Some(metadata)) = (
		read_utf8(name_ptr, name_len),
		read_utf8(desc_ptr, desc_len),
		read_utf8(metadata_ptr, metadata_len),
	) else {
		set_error("invalid set-info string");
		return 0;
	};

	let default_permission = if default_permission_bits == 0 {
		None
	} else {
		match permissions_from_bits(default_permission_bits) {
			Ok(permissions) => Some(permissions),
			Err(err) => {
				set_error(err);
				return 0;
			}
		}
	};

	let operation: Operation = SetInfo {
		name,
		description,
		metadata,
		default_permission,
	}
	.into();

	let id = next_id();
	with_state(|s| {
		s.operations.insert(id, operation);
	});
	id
}

#[no_mangle]
pub extern "C" fn kn_create_send_operation(
	to_id: u64,
	token_id: u64,
	amount_ptr: u32,
	amount_len: u32,
) -> u64 {
	clear_error();
	let Some(amount_str) = read_utf8(amount_ptr, amount_len) else {
		set_error("invalid amount string pointer");
		return 0;
	};
	let amount = match Amount::from_str(&amount_str) {
		Ok(amount) => amount,
		Err(err) => {
			set_error(format!("invalid amount: {err}"));
			return 0;
		}
	};
	let (to, token) = with_state(|s| (s.accounts.get(&to_id).cloned(), s.accounts.get(&token_id).cloned()));
	let (Some(to), Some(token)) = (to, token) else {
		set_error("invalid account handle for send operation");
		return 0;
	};

	let operation: Operation = Send {
		to,
		amount,
		token,
		external: None,
	}
	.into();

	let id = next_id();
	with_state(|s| {
		s.operations.insert(id, operation);
	});
	id
}

#[no_mangle]
pub extern "C" fn kn_free_operation(operation_id: u64) {
	with_state(|s| {
		s.operations.remove(&operation_id);
	});
}

#[no_mangle]
pub extern "C" fn kn_create_block_builder() -> u64 {
	let id = next_id();
	with_state(|s| {
		s.builders.insert(id, BlockBuilder::default());
	});
	id
}

fn consume_builder<F>(builder_id: u64, f: F) -> u64
where
	F: FnOnce(BlockBuilder) -> Option<BlockBuilder>,
{
	clear_error();
	let builder = with_state(|s| s.builders.remove(&builder_id));
	let Some(builder) = builder else {
		set_error("invalid builder handle");
		return 0;
	};
	match f(builder) {
		Some(next) => {
			let id = next_id();
			with_state(|s| {
				s.builders.insert(id, next);
			});
			id
		}
		None => 0,
	}
}

#[no_mangle]
pub extern "C" fn kn_block_builder_set_version(builder_id: u64, version: i32) -> u64 {
	consume_builder(builder_id, |builder| {
		let version = match version {
			1 => BlockVersion::V1,
			2 => BlockVersion::V2,
			_ => {
				set_error("invalid block version");
				return None;
			}
		};
		Some(builder.with_version(version))
	})
}

#[no_mangle]
pub extern "C" fn kn_block_builder_set_network(builder_id: u64, network: u64) -> u64 {
	consume_builder(builder_id, |builder| Some(builder.with_network(network)))
}

#[no_mangle]
pub extern "C" fn kn_block_builder_set_purpose(builder_id: u64, purpose: i32) -> u64 {
	consume_builder(builder_id, |builder| {
		let purpose = match purpose {
			0 => BlockPurpose::Generic,
			1 => BlockPurpose::Fee,
			_ => {
				set_error("invalid block purpose");
				return None;
			}
		};
		Some(builder.with_purpose(purpose))
	})
}

#[no_mangle]
pub extern "C" fn kn_block_builder_set_account(builder_id: u64, account_id: u64) -> u64 {
	let account = with_state(|s| s.accounts.get(&account_id).cloned());
	let Some(account) = account else {
		set_error("invalid account handle");
		return 0;
	};
	consume_builder(builder_id, |builder| Some(builder.with_account(account)))
}

#[no_mangle]
pub extern "C" fn kn_block_builder_set_signer(builder_id: u64, signer_id: u64) -> u64 {
	let signer = with_state(|s| s.accounts.get(&signer_id).cloned());
	let Some(signer) = signer else {
		set_error("invalid signer handle");
		return 0;
	};
	consume_builder(builder_id, |builder| Some(builder.with_signer(Signer::Single(signer))))
}

#[no_mangle]
pub extern "C" fn kn_block_builder_set_multisig_signer(
	builder_id: u64,
	multisig_id: u64,
	signer_ids_ptr: u32,
	signer_count: u32,
) -> u64 {
	let address = with_state(|s| s.accounts.get(&multisig_id).cloned());
	let Some(address) = address else {
		set_error("invalid multisig handle");
		return 0;
	};
	let Some(signer_ids) = read_u64_vec(signer_ids_ptr, signer_count) else {
		set_error("invalid signer id list pointer");
		return 0;
	};
	let signers = with_state(|s| {
		let mut out = Vec::with_capacity(signer_ids.len());
		for id in signer_ids {
			let Some(account) = s.accounts.get(&id).cloned() else {
				return None;
			};
			out.push(Signer::Single(account));
		}
		Some(out)
	});
	let Some(signers) = signers else {
		set_error("invalid signer handle in multisig signer list");
		return 0;
	};
	consume_builder(builder_id, |builder| Some(builder.with_signer(Signer::Multisig { address, signers })))
}

#[no_mangle]
pub extern "C" fn kn_block_builder_set_previous(builder_id: u64, hash_ptr: u32, hash_len: u32) -> u64 {
	let Some(bytes) = read_vec(hash_ptr, hash_len) else {
		set_error("invalid previous hash pointer");
		return 0;
	};
	let Ok(bytes) = <[u8; 32]>::try_from(bytes.as_slice()) else {
		set_error("previous hash must be 32 bytes");
		return 0;
	};
	consume_builder(builder_id, |builder| Some(builder.with_previous(BlockHash::from(bytes))))
}

#[no_mangle]
pub extern "C" fn kn_block_builder_set_no_previous(builder_id: u64) -> u64 {
	consume_builder(builder_id, |builder| Some(builder.as_opening()))
}

#[no_mangle]
pub extern "C" fn kn_block_builder_add_operation(builder_id: u64, operation_id: u64) -> u64 {
	let operation = with_state(|s| s.operations.get(&operation_id).cloned());
	let Some(operation) = operation else {
		set_error("invalid operation handle");
		return 0;
	};
	consume_builder(builder_id, |builder| Some(builder.with_operation(operation)))
}

#[no_mangle]
pub extern "C" fn kn_block_builder_build(builder_id: u64) -> u64 {
	clear_error();
	let builder = with_state(|s| s.builders.remove(&builder_id));
	let Some(builder) = builder else {
		set_error("invalid builder handle");
		return 0;
	};
	match builder.build() {
		Ok(unsigned) => {
			let id = next_id();
			with_state(|s| {
				s.unsigned_blocks.insert(id, unsigned);
			});
			id
		}
		Err(err) => {
			set_error(err);
			0
		}
	}
}

#[no_mangle]
pub extern "C" fn kn_free_block_builder(builder_id: u64) {
	with_state(|s| {
		s.builders.remove(&builder_id);
	});
}

#[no_mangle]
pub extern "C" fn kn_unsigned_block_get_hash(unsigned_id: u64) -> u64 {
	clear_error();
	with_state(|s| match s.unsigned_blocks.get(&unsigned_id) {
		Some(unsigned) => leak_bytes(unsigned.hash().as_bytes().to_vec()),
		None => {
			s.last_error = Some("invalid unsigned block handle".to_owned());
			0
		}
	})
}

#[no_mangle]
pub extern "C" fn kn_unsigned_block_get_hash_string(unsigned_id: u64) -> u64 {
	clear_error();
	with_state(|s| match s.unsigned_blocks.get(&unsigned_id) {
		Some(unsigned) => leak_bytes(unsigned.hash().to_string().into_bytes()),
		None => {
			s.last_error = Some("invalid unsigned block handle".to_owned());
			0
		}
	})
}

#[no_mangle]
pub extern "C" fn kn_unsigned_block_get_signers(unsigned_id: u64) -> u64 {
	clear_error();
	with_state(|s| {
		let Some(unsigned) = s.unsigned_blocks.get(&unsigned_id) else {
			s.last_error = Some("invalid unsigned block handle".to_owned());
			return 0;
		};
		let signers = unsigned.required_signers();
		let mut bytes = Vec::new();
		bytes.extend_from_slice(&(signers.len() as u32).to_be_bytes());
		for signer in signers {
			let public_key = signer.to_public_key_with_type();
			bytes.extend_from_slice(&(public_key.len() as u32).to_be_bytes());
			bytes.extend_from_slice(&public_key);
		}
		leak_bytes(bytes)
	})
}

#[no_mangle]
pub extern "C" fn kn_unsigned_block_sign(unsigned_id: u64) -> u64 {
	clear_error();
	let unsigned = with_state(|s| s.unsigned_blocks.remove(&unsigned_id));
	let Some(unsigned) = unsigned else {
		set_error("invalid unsigned block handle");
		return 0;
	};
	match unsigned.sign() {
		Ok(block) => {
			let id = next_id();
			with_state(|s| {
				s.signed_blocks.insert(id, block);
			});
			id
		}
		Err(err) => {
			set_error(err);
			0
		}
	}
}

#[no_mangle]
pub extern "C" fn kn_free_unsigned_block(unsigned_id: u64) {
	with_state(|s| {
		s.unsigned_blocks.remove(&unsigned_id);
	});
}

#[no_mangle]
pub extern "C" fn kn_signed_block_get_hash(signed_id: u64) -> u64 {
	clear_error();
	with_state(|s| match s.signed_blocks.get(&signed_id) {
		Some(block) => leak_bytes(block.hash().as_bytes().to_vec()),
		None => {
			s.last_error = Some("invalid signed block handle".to_owned());
			0
		}
	})
}

#[no_mangle]
pub extern "C" fn kn_signed_block_get_hash_string(signed_id: u64) -> u64 {
	clear_error();
	with_state(|s| match s.signed_blocks.get(&signed_id) {
		Some(block) => leak_bytes(block.hash().to_string().into_bytes()),
		None => {
			s.last_error = Some("invalid signed block handle".to_owned());
			0
		}
	})
}

#[no_mangle]
pub extern "C" fn kn_signed_block_get_account_string(signed_id: u64) -> u64 {
	clear_error();
	with_state(|s| match s.signed_blocks.get(&signed_id) {
		Some(block) => leak_bytes(block.data().account().to_string().into_bytes()),
		None => {
			s.last_error = Some("invalid signed block handle".to_owned());
			0
		}
	})
}

#[no_mangle]
pub extern "C" fn kn_signed_block_to_bytes(signed_id: u64) -> u64 {
	clear_error();
	with_state(|s| match s.signed_blocks.get(&signed_id) {
		Some(block) => leak_bytes(block.to_bytes().to_vec()),
		None => {
			s.last_error = Some("invalid signed block handle".to_owned());
			0
		}
	})
}

#[no_mangle]
pub extern "C" fn kn_free_signed_block(signed_id: u64) {
	with_state(|s| {
		s.signed_blocks.remove(&signed_id);
	});
}

#[no_mangle]
pub extern "C" fn kn_create_vote_staple(
	block_ptr_len_array_ptr: u32,
	block_count: u32,
	vote_ptr_len_array_ptr: u32,
	vote_count: u32,
) -> u64 {
	clear_error();
	let Some(block_pairs) = read_ptr_len_array(block_ptr_len_array_ptr, block_count) else {
		set_error("invalid block ptr/len array");
		return 0;
	};
	let Some(vote_pairs) = read_ptr_len_array(vote_ptr_len_array_ptr, vote_count) else {
		set_error("invalid vote ptr/len array");
		return 0;
	};

	let mut blocks = Vec::with_capacity(block_pairs.len());
	for (ptr, len) in block_pairs {
		let Some(raw) = read_vec(ptr, len) else {
			set_error("invalid block bytes pointer");
			return 0;
		};
		let block = match Block::try_from(raw.as_slice()) {
			Ok(block) => block,
			Err(err) => {
				set_error(err);
				return 0;
			}
		};
		blocks.push(block);
	}

	let mut votes = Vec::with_capacity(vote_pairs.len());
	for (ptr, len) in vote_pairs {
		let Some(raw) = read_vec(ptr, len) else {
			set_error("invalid vote bytes pointer");
			return 0;
		};
		let vote = match Vote::verify(raw) {
			Ok(vote) => vote,
			Err(err) => {
				set_error(err);
				return 0;
			}
		};
		votes.push(vote);
	}

	let staple = match VoteStapleBuilder::new().add_blocks(blocks).add_votes(votes).build() {
		Ok(staple) => staple,
		Err(err) => {
			set_error(err);
			return 0;
		}
	};
	leak_bytes(staple.as_bytes().to_vec())
}

#[no_mangle]
pub extern "C" fn kn_vote_select_fee(
	vote_ptr: u32,
	vote_len: u32,
	preferred_token_account_id: u64,
) -> u64 {
	clear_error();
	let Some(vote_bytes) = read_vec(vote_ptr, vote_len) else {
		set_error("invalid vote bytes pointer");
		return 0;
	};
	let vote = match Vote::from_serialized(vote_bytes) {
		Ok(vote) => vote,
		Err(err) => {
			set_error(err);
			return 0;
		}
	};
	let Some(fees) = vote.fees() else {
		return leak_bytes(Vec::new());
	};
	let preferred_token = with_state(|s| s.accounts.get(&preferred_token_account_id).cloned());
	let Some(preferred_token) = preferred_token else {
		set_error("invalid preferred token account handle");
		return 0;
	};
	let preferred_token_string = preferred_token.to_string();

	let mut selected = None;
	for fee in fees.entries() {
		if fee.token.is_none()
			|| fee
				.token
				.as_ref()
				.map(|token| token.to_string() == preferred_token_string)
				.unwrap_or(false)
		{
			selected = Some(fee.clone());
			break;
		}
	}
	if selected.is_none() {
		selected = fees.entries().next().cloned();
	}
	let Some(selected) = selected else {
		return leak_bytes(Vec::new());
	};

	let pay_to = selected.pay_to.unwrap_or_else(|| vote.issuer().clone());
	let token = selected.token.unwrap_or(preferred_token);
	let payload = format!(
		"{}\n{}\n{}",
		selected.amount,
		pay_to.to_string(),
		token.to_string()
	);
	leak_bytes(payload.into_bytes())
}
