using System.Text.Json;
using KeetaNet.Anchor;

namespace KeetaNet.Examples.Anchor.AssetMovement;

/// <summary>
/// Parses typed asset-movement blockers from wasm error payloads and prints the
/// guidance messages the deposit/withdraw examples use when KYC share or
/// onboarding is still required.
/// </summary>
public static class AssetMovementBlockerErrors
{
	private static readonly JsonSerializerOptions BlockerJsonOptions = new()
	{
		PropertyNameCaseInsensitive = true,
	};

	private static readonly JsonSerializerOptions DisplayJsonOptions = new() { WriteIndented = true };

	public const string KycShareNeededCode = "KEETA_ANCHOR_ASSET_MOVEMENT_KYC_SHARE_NEEDED";
	public const string UserActionNeededCode = "KEETA_ANCHOR_ASSET_MOVEMENT_USER_ACTION_NEEDED";

	/// <summary>
	/// When <paramref name="error"/> is a recognized blocker, prints guidance on
	/// stderr and returns true so the caller can exit early.
	/// </summary>
	public static bool TryReport(KeetaException error, string goal)
	{
		if (error.Code == KycShareNeededCode)
		{
			Console.Error.WriteLine($"KYC attributes must be shared with the provider before {goal}.");
			Console.Error.WriteLine("Complete KYC sharing (see anchor/kyc-client-sharekyc), then run this example again.");
			return true;
		}

		if (error.Code == UserActionNeededCode)
		{
			AssetUserActionNeededBlocker userActionNeeded = ParseUserActionNeeded(error);
			Console.Error.WriteLine($"Provider onboarding steps are still required before {goal}.");
			Console.Error.WriteLine("Complete the actions below (see anchor/kyc-client-sharekyc), then run this example again.");
			Console.Error.WriteLine(JsonSerializer.Serialize(userActionNeeded.ActionsNeeded, DisplayJsonOptions));
			return true;
		}

		return false;
	}

	public static AssetKycShareNeededBlocker ParseKycShareNeeded(KeetaException error) =>
		JsonSerializer.Deserialize<AssetKycShareNeededBlocker>(BlockerPayload(error), BlockerJsonOptions)
		?? throw new InvalidOperationException("could not decode a KYC share blocker from the anchor response", error);

	public static AssetUserActionNeededBlocker ParseUserActionNeeded(KeetaException error) =>
		JsonSerializer.Deserialize<AssetUserActionNeededBlocker>(BlockerPayload(error), BlockerJsonOptions)
		?? throw new InvalidOperationException("could not decode a user-action blocker from the anchor response", error);

	private static string BlockerPayload(KeetaException error)
	{
		string prefix = $"{error.Code}: ";
		return error.Message.StartsWith(prefix, StringComparison.Ordinal)
			? error.Message[prefix.Length..]
			: error.Message;
	}
}
