using KeetaNet.Anchor;

namespace KeetaNet.Examples.Ledger;

/// <summary>An unsigned block awaiting signatures from its required signers.</summary>
public sealed class UnsignedBlock : GuestHandle
{
	private bool _signedAway;

	private UnsignedBlock(WasmRuntime runtime, int handle)
		: base(runtime, handle)
	{
	}

	internal static UnsignedBlock Adopt(WasmRuntime runtime, int handle) =>
		new(runtime, GuestInterop.TakeHandle(runtime, handle));

	/// <summary>Sign with the private keys held by the required signers, sealing the block.</summary>
	public SignedBlock Sign()
	{
		ObjectDisposedException.ThrowIf(_signedAway, this);
		_signedAway = true;
		int signed = GuestInterop.Run(Runtime, () =>
			GuestInterop.TakeHandle(Runtime, GuestInterop.Invoke(Runtime, "keeta_unsigned_sign", Handle)));
		return SignedBlock.Adopt(Runtime, signed);
	}

	protected override void Release(int handle)
	{
		if (!_signedAway)
		{
			GuestInterop.Free(Runtime, "keeta_unsigned_free", handle);
		}
	}
}
