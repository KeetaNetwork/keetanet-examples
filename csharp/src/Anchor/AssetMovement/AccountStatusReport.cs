using System.Text.Json;
using KeetaNet.Anchor;

namespace KeetaNet.Examples.Anchor.AssetMovement;

/// <summary>
/// Prints guidance for the SDK's typed <see cref="AssetMovementBlocker"/>s an
/// example cannot resolve on its own. Used by the flows that assume KYC and
/// onboarding are already complete (they report, rather than auto-onboard).
/// </summary>
public static class AccountStatusReport
{
	private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

	/// <summary>
	/// Whether <paramref name="status"/> is clear to proceed. When it is not, the
	/// blockers are described on stderr and the caller should stop.
	/// </summary>
	public static bool IsReady(AssetAccountStatus status, string goal)
	{
		if (!status.ActionRequired || status.Blockers is not { } blockers)
		{
			return true;
		}

		if (blockers.OfType<AssetKycShareNeededBlocker>().Any())
		{
			Console.Error.WriteLine($"KYC attributes must be shared with the provider before {goal}.");
			Console.Error.WriteLine("Complete KYC sharing (see anchor/kyc-client-sharekyc), then run this example again.");
			return false;
		}

		if (blockers.OfType<AssetUserActionNeededBlocker>().FirstOrDefault() is { } userActionNeeded)
		{
			Console.Error.WriteLine($"Provider onboarding steps are still required before {goal}.");
			Console.Error.WriteLine("Complete the actions below (see anchor/kyc-client-sharekyc), then run this example again.");
			Console.Error.WriteLine(JsonSerializer.Serialize(userActionNeeded.ActionsNeeded, JsonOptions));
			return false;
		}

		Console.Error.WriteLine(
			$"The provider reports the account is not ready before {goal}: "
			+ string.Join(", ", blockers.Select(blocker => blocker.GetType().Name)));
		return false;
	}
}
