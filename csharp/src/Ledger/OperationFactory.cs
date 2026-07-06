using System.Globalization;
using System.Numerics;
using KeetaNet.Anchor;
using KeetaNet.Anchor.Crypto;

namespace KeetaNet.Examples.Ledger;

/// <summary>Creates block operations through the wasm core.</summary>
public sealed class OperationFactory(WasmRuntime runtime)
{
	private readonly WasmRuntime _runtime = runtime;

	/// <summary>A <c>SEND</c> operation transferring <paramref name="amount"/> of <paramref name="token"/> to <paramref name="to"/>.</summary>
	public Operation Send(Account to, string amount, Account token, string? external = null) =>
		GuestInterop.Run(_runtime, () =>
		{
			using var args = new GuestInterop.ArgumentScope(_runtime);
			GuestInterop.Argument amountArg = args.Write(amount);
			GuestInterop.Argument externalArg = args.Write(external ?? string.Empty);
			int handle = GuestInterop.Invoke(
				_runtime,
				"keeta_op_send",
				GuestInterop.AccountHandle(to),
				amountArg.Pointer,
				amountArg.Length,
				GuestInterop.AccountHandle(token),
				externalArg.Pointer,
				externalArg.Length);

			return Operation.Adopt(_runtime, handle);
		});

	/// <summary>A <c>MANAGE_CERTIFICATE</c> add operation for hex-DER <paramref name="certificateDerHex"/> and optional hex-DER intermediates.</summary>
	public Operation ManageCertificateAdd(string certificateDerHex, IReadOnlyList<string> intermediateDerHex) =>
		GuestInterop.Run(_runtime, () =>
		{
			using var args = new GuestInterop.ArgumentScope(_runtime);
			GuestInterop.Argument certificateArg = args.Write(certificateDerHex);
			GuestInterop.Argument intermediatesArg = args.Write(string.Join('\n', intermediateDerHex));
			int handle = GuestInterop.Invoke(
				_runtime,
				"keeta_op_manage_certificate_add",
				certificateArg.Pointer,
				certificateArg.Length,
				intermediatesArg.Pointer,
				intermediatesArg.Length);

			return Operation.Adopt(_runtime, handle);
		});

	/// <summary>A <c>MODIFY_PERMISSIONS</c> add operation for <paramref name="principal"/>.</summary>
	public Operation ModifyPermissions(
		Account principal,
		string basePermissionBitmap,
		string externalPermissionBitmap,
		string method = "add",
		Account? target = null) =>
		GuestInterop.Run(_runtime, () =>
		{
			using var args = new GuestInterop.ArgumentScope(_runtime);
			GuestInterop.Argument baseArg = args.Write(ToPermissionHex(basePermissionBitmap));
			GuestInterop.Argument externalArg = args.Write(ToPermissionHex(externalPermissionBitmap));
			int permissionsHandle = GuestInterop.TakeHandle(
				_runtime,
				GuestInterop.Invoke(
					_runtime,
					"keeta_permissions_from_bitmaps",
					baseArg.Pointer,
					baseArg.Length,
					externalArg.Pointer,
					externalArg.Length));

			try
			{
				GuestInterop.Argument methodArg = args.Write(method);
				int targetHandle = target is null ? 0 : GuestInterop.AccountHandle(target);
				int handle = GuestInterop.Invoke(
					_runtime,
					"keeta_op_modify_permissions",
					GuestInterop.AccountHandle(principal),
					permissionsHandle,
					methodArg.Pointer,
					methodArg.Length,
					targetHandle);

				return Operation.Adopt(_runtime, handle);
			}
			finally
			{
				GuestInterop.Free(_runtime, "keeta_permissions_free", permissionsHandle);
			}
		});

	internal static string ToDerHex(KeetaNet.Anchor.Crypto.Certificate certificate) =>
		Convert.ToHexString(certificate.ToDer()).ToLowerInvariant();

	private static string ToPermissionHex(string value)
	{
		if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
		{
			return value;
		}

		BigInteger parsed = BigInteger.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
		if (parsed.IsZero)
		{
			return "0x0";
		}

		return "0x" + parsed.ToString("x", CultureInfo.InvariantCulture);
	}
}
