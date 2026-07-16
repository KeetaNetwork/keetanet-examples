using KeetaNet.Anchor;
using KeetaNet.Anchor.Crypto;

namespace KeetaNet.Examples.Ledger;

/// <summary>Fluent builder for an unsigned block.</summary>
public sealed class BlockBuilder : IDisposable
{
	private readonly WasmRuntime _runtime;
	private int _handle;

	internal BlockBuilder(WasmRuntime runtime)
	{
		_runtime = runtime;
		_handle = GuestInterop.Run(runtime, () =>
			GuestInterop.TakeHandle(runtime, GuestInterop.Invoke(runtime, "keeta_builder_new")));
	}

	/// <summary>Set the block version (<c>1</c> or <c>2</c>).</summary>
	public BlockBuilder Version(int version) => Step("keeta_builder_with_version", version);

	/// <summary>Set the network id.</summary>
	public BlockBuilder Network(ulong network) => Step("keeta_builder_with_network", (long)network);

	/// <summary>Set the originating account.</summary>
	public BlockBuilder Account(Account account) => Step("keeta_builder_with_account", GuestInterop.AccountHandle(account));

	/// <summary>Set a single-account signer.</summary>
	public BlockBuilder Signer(Account signer) => Step("keeta_builder_with_signer", GuestInterop.AccountHandle(signer));

	/// <summary>Set the previous block hash (32 bytes).</summary>
	public BlockBuilder Previous(byte[] previousHash) =>
		Step("keeta_builder_with_previous", previousHash);

	/// <summary>Mark the block as an account opening (no previous).</summary>
	public BlockBuilder Opening() => Step("keeta_builder_as_opening");

	/// <summary>Set the block timestamp (Unix milliseconds).</summary>
	public BlockBuilder Date(long unixMillis) => Step("keeta_builder_with_date", unixMillis);

	/// <summary>Set the block purpose (<c>generic</c> or <c>fee</c>).</summary>
	public BlockBuilder Purpose(string purpose) =>
		GuestInterop.Run(_runtime, () =>
		{
			using var args = new GuestInterop.ArgumentScope(_runtime);
			GuestInterop.Argument value = args.Write(purpose);
			return Step(GuestInterop.TakeHandle(_runtime, GuestInterop.Invoke(
				_runtime,
				"keeta_builder_with_purpose",
				Consume(),
				value.Pointer,
				value.Length)));
		});

	/// <summary>Append an operation.</summary>
	public BlockBuilder AddOperation(Operation operation) => Step("keeta_builder_with_operation", operation.RawHandle);

	/// <summary>Build and validate the unsigned block, consuming this builder.</summary>
	public UnsignedBlock Build()
	{
		int unsigned = GuestInterop.Run(_runtime, () =>
			GuestInterop.TakeHandle(_runtime, GuestInterop.Invoke(_runtime, "keeta_builder_build", Consume())));
		return UnsignedBlock.Adopt(_runtime, unsigned);
	}

	private BlockBuilder Step(string export) =>
		Step(GuestInterop.Run(_runtime, () =>
			GuestInterop.TakeHandle(_runtime, GuestInterop.Invoke(_runtime, export, Consume()))));

	private BlockBuilder Step(string export, int arg) =>
		Step(GuestInterop.Run(_runtime, () =>
			GuestInterop.TakeHandle(_runtime, GuestInterop.Invoke(_runtime, export, Consume(), arg))));

	private BlockBuilder Step(string export, long arg) =>
		Step(GuestInterop.Run(_runtime, () =>
			GuestInterop.TakeHandle(_runtime, GuestInterop.Invoke(_runtime, export, Consume(), arg))));

	private BlockBuilder Step(string export, byte[] bytes) =>
		GuestInterop.Run(_runtime, () =>
		{
			using var args = new GuestInterop.ArgumentScope(_runtime);
			GuestInterop.Argument value = args.WriteBytes(bytes);
			return Step(GuestInterop.TakeHandle(_runtime, GuestInterop.Invoke(
				_runtime,
				export,
				Consume(),
				value.Pointer,
				value.Length)));
		});

	private BlockBuilder Step(int next)
	{
		_handle = next;
		return this;
	}

	private int Consume()
	{
		if (_handle == 0)
		{
			throw new InvalidOperationException("block builder has been consumed");
		}

		int current = _handle;
		_handle = 0;
		return current;
	}

	public void Dispose()
	{
		if (_handle == 0)
		{
			return;
		}

		GuestInterop.Run(_runtime, () => GuestInterop.Free(_runtime, "keeta_builder_free", _handle));
		_handle = 0;
	}
}

/// <summary>Creates block builders through the wasm core.</summary>
public sealed class BlockBuilderFactory(WasmRuntime runtime)
{
	/// <summary>Start a new unsigned block.</summary>
	public BlockBuilder Builder() => new(runtime);
}
