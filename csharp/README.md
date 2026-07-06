# Keeta Network Examples (C#)

C# ports of selected [keetanet-examples](../) TypeScript samples, using the anchor-csharp SDK.

Examples use `WasmRuntime`, anchor service clients (such as `KycClient`), and raw HTTP node calls for ledger reads and block publication — mirroring the TypeScript samples

## Prerequisites

- .NET 10 SDK
- Built anchor-csharp wasm core (`make -C ../../anchor-csharp build`) for now

## Quick start

```bash
make help
make build
make anchor/asset-movement-evm-inbound
```