using System.Globalization;
using System.Numerics;
using System.Text.Json;
using KeetaNet.Anchor;
using KeetaNet.Anchor.Crypto;
using CryptoCertificate = KeetaNet.Anchor.Crypto.Certificate;

namespace KeetaNet.Examples.Anchor.AssetMovement;

/// <summary>
/// Translates provider onboarding actions into ledger writes, the port of the
/// reference <c>UserActionNeeded.addOperationsToBuilder</c>.
/// </summary>
public static class UserActions
{
	public static async Task Execute(
		WasmRuntime runtime,
		UserClient userClient,
		KeetaNetwork network,
		AssetUserActionNeededBlocker blocker,
		CancellationToken cancellationToken)
	{
		foreach (JsonElement action in blocker.ActionsNeeded)
		{
			string? type = action.TryGetProperty("type", out JsonElement typeElement)
				? typeElement.GetString()
				: null;

			switch (type)
			{
				case "add-certificate":
					await AddCertificate(runtime, userClient, network, action, cancellationToken).ConfigureAwait(false);
					break;
				case "grant-permission":
					await GrantPermission(runtime, userClient, network, action, cancellationToken).ConfigureAwait(false);
					break;
				case "provider-kyc-flow":
					throw new InvalidOperationException(
						"Provider KYC flow actions must be completed out-of-band before retrying.");
				default:
					throw new InvalidOperationException($"Unsupported onboarding action type: {type ?? "(missing)"}");
			}
		}
	}

	private static async Task AddCertificate(
		WasmRuntime runtime,
		UserClient userClient,
		KeetaNetwork network,
		JsonElement action,
		CancellationToken cancellationToken)
	{
		string certificatePem = action.GetProperty("certificate").GetString()
			?? throw new InvalidOperationException("add-certificate action is missing certificate");

		List<CryptoCertificate> intermediates = new();
		try
		{
			if (action.TryGetProperty("intermediates", out JsonElement bundled)
				&& bundled.ValueKind == JsonValueKind.Array)
			{
				foreach (JsonElement intermediate in bundled.EnumerateArray())
				{
					string intermediatePem = intermediate.GetString()
						?? throw new InvalidOperationException("add-certificate intermediate is not a PEM string");
					intermediates.Add(runtime.Certificates.Parse(intermediatePem));
				}
			}

			using CryptoCertificate certificate = runtime.Certificates.Parse(certificatePem);
			using Account? blockAccount = ReadAccount(runtime, action, "account");
			using UserClient? scoped = ScopeToAccount(runtime, userClient, network, blockAccount);
			UserClient writer = scoped ?? userClient;

			await writer.ModifyCertificate(AdjustMethod.Add, certificate, intermediates, cancellationToken: cancellationToken)
				.ConfigureAwait(false);
		}
		finally
		{
			foreach (CryptoCertificate intermediate in intermediates)
			{
				intermediate.Dispose();
			}
		}
	}

	private static async Task GrantPermission(
		WasmRuntime runtime,
		UserClient userClient,
		KeetaNetwork network,
		JsonElement action,
		CancellationToken cancellationToken)
	{
		JsonElement grant = action.GetProperty("permissionToGrant");
		string principalAddress = grant.GetProperty("principal").GetString()
			?? throw new InvalidOperationException("grant-permission action is missing principal");

		JsonElement bitmaps = grant.GetProperty("permissions");
		if (bitmaps.ValueKind != JsonValueKind.Array || bitmaps.GetArrayLength() != 2)
		{
			throw new InvalidOperationException("grant-permission action is missing permission bitmaps");
		}

		using Account principal = runtime.Accounts.FromPublicKeyString(principalAddress);
		using Permissions permissions = runtime.Blocks.PermissionsFromBitmaps(
			ToHexBitmap(bitmaps[0], "base"),
			ToHexBitmap(bitmaps[1], "external"));
		using Account? target = ReadAccount(runtime, grant, "target");
		using Account? entity = ReadAccount(runtime, grant, "entity");
		using UserClient? scoped = ScopeToAccount(runtime, userClient, network, entity);
		UserClient writer = scoped ?? userClient;

		await writer.UpdatePermissions(
			principal,
			permissions,
			target,
			AdjustMethod.Add,
			cancellationToken: cancellationToken).ConfigureAwait(false);
	}

	/// <summary>
	/// A client operating as <paramref name="account"/> with the caller's
	/// signer, for actions that write to another account's chain (e.g. a
	/// storage account the user administers). Null when no override applies.
	/// </summary>
	private static UserClient? ScopeToAccount(
		WasmRuntime runtime,
		UserClient userClient,
		KeetaNetwork network,
		Account? account)
	{
		if (account is null)
		{
			return null;
		}

		return runtime.CreateUserClient(network, userClient.Signer, account: account);
	}

	private static Account? ReadAccount(WasmRuntime runtime, JsonElement element, string name) =>
		element.TryGetProperty(name, out JsonElement address) && address.ValueKind == JsonValueKind.String
			? runtime.Accounts.FromPublicKeyString(address.GetString()!)
			: null;

	/// <summary>
	/// The anchor serializes permission bitmaps as decimal bigint strings;
	/// the core decoder expects hex.
	/// </summary>
	private static string ToHexBitmap(JsonElement bitmap, string label)
	{
		string? decimalDigits = bitmap.GetString();
		if (decimalDigits is null
			|| !BigInteger.TryParse(decimalDigits, NumberStyles.Integer, CultureInfo.InvariantCulture, out BigInteger value))
		{
			throw new InvalidOperationException($"grant-permission {label} bitmap is not a decimal bigint string");
		}

		return "0x" + value.ToString("x", CultureInfo.InvariantCulture);
	}
}
