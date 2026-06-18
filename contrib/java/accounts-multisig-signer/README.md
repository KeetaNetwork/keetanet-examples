# Keetanet Java JNI Multisig Example

This example demonstrates how to use the Keetanet Rust crates from Java via
JNI (Java Native Interface). It mirrors the functionality of the TypeScript
`src/client/accounts-multisig-signer.ts` example.

## What it demonstrates

1. Generating a random seed and deriving accounts from it (ED25519)
2. Deriving a multisig identifier and a token identifier from an account
3. Signing and verifying messages
4. Building, hashing, signing, and transmitting blocks:
   - `identifierBlock` — a `CREATE_IDENTIFIER` operation that creates a
     2-of-3 multisig (plus `MODIFY_PERMISSIONS` granting it `ADMIN`),
     signed by the user account
   - `multisigExampleBlock` — a `SET_INFO` operation on a custom token,
     signed by the multisig using only 2 of its 3 signers (the quorum)
5. Transmitting both signed blocks as vote staples through `keetanetwork-client`
6. Requesting testnet faucet funds before transmitting (same as the TypeScript example)

## Layout

* `src/main/java/network/keeta/examples/` — Java sources:
  - `KeetaNetJNI.java` — the raw JNI bindings
  - `Account.java`, `Block.java`, `UserClient.java`, `Permissions.java` — thin wrappers that
    manage native memory (via `AutoCloseable`) and provide a friendlier API
  - `AccountsMultisigSigner.java` — the example program
* `src/main/rust/lib.rs` — the Rust JNI shim around the `keetanetwork-*` crates
* `Cargo.toml` / `Makefile` — build configuration

## Requirements

* Rust 1.70+ (cargo)
* Java JDK 17+
* Make

## Building and running

```sh
make run
```

Other targets: `make all` (build only), `make test`, `make clean`,
`make distclean`, `make help`.
