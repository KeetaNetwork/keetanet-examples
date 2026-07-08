using System.Text.Json;
using KeetaNet.Anchor;
using KeetaNet.Anchor.Crypto;
using CryptoCertificate = KeetaNet.Anchor.Crypto.Certificate;
using KeetaNet.Examples.Ledger;
using UserClient = KeetaNet.Examples.Network.UserClient;

namespace KeetaNet.Examples.Anchor.AssetMovement;

/// <summary>Translates provider onboarding actions into ledger operations.</summary>
public static class UserActions
{
	public static async Task ExecuteAsync(
		WasmRuntime runtime,
		UserClient userClient,
		AssetUserActionNeededBlocker blocker,
		CancellationToken cancellationToken)
	{
		List<Operation> operations = new();
		Account signer = userClient.Signer
			?? throw new InvalidOperationException("No signer available in a read-only UserClient");
		Account blockAccount = signer;

		try
		{
			foreach (JsonElement action in blocker.ActionsNeeded)
			{
				string? type = action.TryGetProperty("type", out JsonElement typeElement)
					? typeElement.GetString()
					: null;

				switch (type)
				{
					case "add-certificate":
						operations.Add(BuildAddCertificateOperation(runtime, action));
						blockAccount = ResolveBlockAccount(runtime, action) ?? blockAccount;
						break;
					case "grant-permission":
						operations.Add(BuildGrantPermissionOperation(runtime, action));
						blockAccount = ResolveBlockAccount(runtime, action) ?? blockAccount;
						break;
					case "provider-kyc-flow":
						throw new InvalidOperationException(
							"Provider KYC flow actions must be completed out-of-band before retrying.");
					default:
						throw new InvalidOperationException($"Unsupported onboarding action type: {type ?? "(missing)"}");
				}
			}

			if (operations.Count == 0)
			{
				return;
			}

			await userClient.PublishOperationsAsync(runtime, operations, blockAccount, cancellationToken).ConfigureAwait(false);
		}
		finally
		{
			foreach (Operation operation in operations)
			{
				operation.Dispose();
			}
		}
	}

	private static Operation BuildAddCertificateOperation(WasmRuntime runtime, JsonElement action)
	{
		string certificatePem = action.GetProperty("certificate").GetString()
			?? throw new InvalidOperationException("add-certificate action is missing certificate");

		List<string> intermediateDerHex = new();
		if (action.TryGetProperty("intermediates", out JsonElement intermediates)
			&& intermediates.ValueKind == JsonValueKind.Array)
		{
			foreach (JsonElement intermediate in intermediates.EnumerateArray())
			{
				string intermediatePem = intermediate.GetString()
					?? throw new InvalidOperationException("add-certificate intermediate is not a PEM string");
				using CryptoCertificate intermediateCertificate = runtime.Certificates.Parse(intermediatePem);
				intermediateDerHex.Add(OperationFactory.ToDerHex(intermediateCertificate));
			}
		}

		using CryptoCertificate certificate = runtime.Certificates.Parse(certificatePem);
		return runtime.Operations().ManageCertificateAdd(OperationFactory.ToDerHex(certificate), intermediateDerHex);
	}

	private static Operation BuildGrantPermissionOperation(WasmRuntime runtime, JsonElement action)
	{
		JsonElement permission = action.GetProperty("permissionToGrant");
		string principalAddress = permission.GetProperty("principal").GetString()
			?? throw new InvalidOperationException("grant-permission action is missing principal");

		JsonElement permissionsElement = permission.GetProperty("permissions");
		if (permissionsElement.ValueKind != JsonValueKind.Array || permissionsElement.GetArrayLength() != 2)
		{
			throw new InvalidOperationException("grant-permission action is missing permission bitmaps");
		}

		string baseBitmap = permissionsElement[0].GetString()
			?? throw new InvalidOperationException("grant-permission base bitmap is missing");
		string externalBitmap = permissionsElement[1].GetString()
			?? throw new InvalidOperationException("grant-permission external bitmap is missing");

		using Account principal = runtime.Accounts.FromAccount(principalAddress);
		if (permission.TryGetProperty("target", out JsonElement targetElement)
			&& targetElement.ValueKind == JsonValueKind.String)
		{
			using Account target = runtime.Accounts.FromAccount(targetElement.GetString()!);
			return runtime.Operations().ModifyPermissions(principal, baseBitmap, externalBitmap, target: target);
		}

		return runtime.Operations().ModifyPermissions(principal, baseBitmap, externalBitmap);
	}

	private static Account? ResolveBlockAccount(WasmRuntime runtime, JsonElement action)
	{
		if (action.TryGetProperty("account", out JsonElement accountElement)
			&& accountElement.ValueKind == JsonValueKind.String)
		{
			return runtime.Accounts.FromAccount(accountElement.GetString()!);
		}

		if (action.TryGetProperty("permissionToGrant", out JsonElement permission)
			&& permission.TryGetProperty("entity", out JsonElement entityElement)
			&& entityElement.ValueKind == JsonValueKind.String)
		{
			return runtime.Accounts.FromAccount(entityElement.GetString()!);
		}

		return null;
	}
}
