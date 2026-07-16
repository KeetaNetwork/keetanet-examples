using KeetaNet.Anchor;

namespace KeetaNet.Examples.Ledger;

/// <summary>A block operation held as a guest handle.</summary>
public sealed class Operation : GuestHandle
{
	private Operation(WasmRuntime runtime, int handle)
		: base(runtime, handle)
	{
	}

	internal static Operation Adopt(WasmRuntime runtime, int handle) =>
		new(runtime, GuestInterop.TakeHandle(runtime, handle));

	protected override void Release(int handle) => GuestInterop.Free(Runtime, "keeta_op_free", handle);
}
