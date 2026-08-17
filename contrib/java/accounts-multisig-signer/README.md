# Keetanet Java WASM Multisig Example

This example demonstrates how to use the Keetanet Rust crates from Java via
WASM (loaded with Chicory). It mirrors the functionality of the TypeScript
`src/client/accounts-multisig-signer.ts` example.

## What it demonstrates

1. Generating a random seed and deriving accounts from it (default ECDSA_SECP256K1, matching the TypeScript example)
2. Deriving a multisig identifier and a token identifier from an account
3. Signing and verifying messages
4. Building, hashing, signing, and transmitting blocks:
   - `identifierBlock` — a `CREATE_IDENTIFIER` operation that creates a
     2-of-3 multisig (plus `MODIFY_PERMISSIONS` granting it `ADMIN`),
     signed by the user account
   - `multisigExampleBlock` — a `SET_INFO` operation on a custom token,
     signed by the multisig using only 2 of its 3 signers (the quorum)
5. Transmitting both signed blocks as vote staples using Java HTTP networking (`/vote`, `/node/publish`)
   with vote staple construction done by Rust/WASM
6. Handling `LEDGER_SUCCESSOR_VOTE_EXISTS` by waiting for representative vote expiry and retrying transmit
6. Requesting testnet faucet funds before transmitting (same as the TypeScript example)

## Layout

* `src/main/java/network/keeta/examples/` — Java sources:
  - `KeetaNetJNI.java`, `KeetaNetWasmBridge.java` — WASM bridge bindings
  - `Account.java`, `Block.java`, `UserClient.java`, `Permissions.java` — thin wrappers that
    manage Rust-side handles (via `AutoCloseable`) and provide a friendlier API
  - `AccountsMultisigSigner.java` — the example program
* `wasm-bridge/src/lib.rs` — the Rust WASM shim around `keetanetwork-*` crates
* `Cargo.toml` / `Makefile` — build configuration

## Requirements

* Rust 1.70+ (cargo, plus target `wasm32-wasip1`)
* Java JDK 17+
* Make
* curl (to fetch Chicory jars)

## Building and running

```sh
make run
```

Other targets: `make all` (build only), `make test`, `make clean`,
`make distclean`, `make help`.
