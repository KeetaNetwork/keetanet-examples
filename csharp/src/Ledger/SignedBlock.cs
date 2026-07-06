using KeetaNet.Anchor;
using KeetaNet.Anchor.Crypto;

namespace KeetaNet.Examples.Ledger;

/// <summary>A signed block ready for transmission.</summary>
public sealed class SignedBlock : GuestHandle
{
	private SignedBlock(WasmRuntime runtime, int handle)
		: base(runtime, handle)
	{
	}

	internal static SignedBlock Adopt(WasmRuntime runtime, int handle) =>
		new(runtime, GuestInterop.TakeHandle(runtime, handle));

	/// <summary>The block hash (hex).</summary>
	public string HashHex =>
		GuestInterop.Run(Runtime, () => GuestInterop.TakeString(Runtime, GuestInterop.Invoke(Runtime, "keeta_block_hash", Handle)));

	/// <summary>The originating account address.</summary>
	public string AccountAddress
	{
		get
		{
			int accountHandle = GuestInterop.Run(Runtime, () =>
				GuestInterop.TakeHandle(Runtime, GuestInterop.Invoke(Runtime, "keeta_block_account", Handle)));
			try
			{
				return GuestInterop.Run(Runtime, () =>
					GuestInterop.TakeString(Runtime, GuestInterop.Invoke(Runtime, "keeta_account_address", accountHandle)));
			}
			finally
			{
				GuestInterop.Run(Runtime, () => GuestInterop.Free(Runtime, "keeta_account_free", accountHandle));
			}
		}
	}

	/// <summary>The raw transport bytes.</summary>
	public byte[] ToBytes() =>
		GuestInterop.Run(Runtime, () => GuestInterop.TakeBytes(Runtime, GuestInterop.Invoke(Runtime, "keeta_block_to_bytes", Handle)));

	protected override void Release(int handle) => GuestInterop.Free(Runtime, "keeta_block_free", handle);
}
