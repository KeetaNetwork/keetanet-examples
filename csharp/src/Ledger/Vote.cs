using KeetaNet.Anchor;
using KeetaNet.Anchor.Crypto;

namespace KeetaNet.Examples.Ledger;

/// <summary>A vote decoded from transport bytes.</summary>
public sealed class Vote : GuestHandle
{
	private Vote(WasmRuntime runtime, int handle)
		: base(runtime, handle)
	{
	}

	/// <summary>Decode a vote from its transport bytes.</summary>
	public static Vote FromBytes(WasmRuntime runtime, byte[] voteBytes) =>
		GuestInterop.Run(runtime, () =>
		{
			using var args = new GuestInterop.ArgumentScope(runtime);
			GuestInterop.Argument vote = args.WriteBytes(voteBytes);
			int handle = GuestInterop.Invoke(runtime, "keeta_vote_from_bytes", vote.Pointer, vote.Length);
			return Adopt(runtime, handle);
		});

	internal static Vote Adopt(WasmRuntime runtime, int handle) =>
		new(runtime, GuestInterop.TakeHandle(runtime, handle));

	/// <summary>
	/// Build a fee-send operation for this vote, or <see langword="null"/> when no fee is owed.
	/// </summary>
	public Operation? CreateFeeSend(Account baseToken)
	{
		int handle = GuestInterop.Run(Runtime, () =>
			GuestInterop.Invoke(Runtime, "keeta_fee_send", Handle, GuestInterop.AccountHandle(baseToken), 0, 0));

		return handle == 0 ? null : Operation.Adopt(Runtime, handle);
	}

	/// <summary>Assemble a staple over <paramref name="blocks"/> plus this vote.</summary>
	public byte[] BuildStaple(IReadOnlyList<SignedBlock> blocks, long unixMillis) =>
		GuestInterop.Run(Runtime, () =>
		{
			using var args = new GuestInterop.ArgumentScope(Runtime);
			int[] blockHandles = blocks.Select(block => block.RawHandle).ToArray();
			GuestInterop.Argument blocksArg = args.WriteHandles(blockHandles);
			GuestInterop.Argument votesArg = args.WriteHandles(new[] { Handle });
			int result = GuestInterop.Invoke(
				Runtime,
				"keeta_vote_staple_build",
				blocksArg.Pointer,
				blockHandles.Length * 4,
				votesArg.Pointer,
				4,
				unixMillis);

			return GuestInterop.TakeBytes(Runtime, result);
		});

	protected override void Release(int handle) => GuestInterop.Free(Runtime, "keeta_vote_free", handle);
}
