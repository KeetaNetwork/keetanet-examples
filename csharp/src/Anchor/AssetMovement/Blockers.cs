using System.Text.Json;
using KeetaNet.Anchor;

namespace KeetaNet.Examples.Anchor.AssetMovement;

/// <summary>Typed asset-movement blockers decoded from provider account status.</summary>
public static class Blockers
{
	public const string KycShareNeededCode = "KEETA_ANCHOR_ASSET_MOVEMENT_KYC_SHARE_NEEDED";
	public const string UserActionNeededCode = "KEETA_ANCHOR_ASSET_MOVEMENT_USER_ACTION_NEEDED";

	public static KycShareNeededBlocker? FindKycShareNeeded(AssetAccountStatus status) =>
		status.Blockers?
			.Select(TryParseKycShareNeeded)
			.FirstOrDefault(blocker => blocker is not null);

	public static UserActionNeededBlocker? FindUserActionNeeded(AssetAccountStatus status) =>
		status.Blockers?
			.Select(TryParseUserActionNeeded)
			.FirstOrDefault(blocker => blocker is not null);

	public static KycShareNeededBlocker? TryParseKycShareNeeded(JsonElement blocker)
	{
		if (blocker.TryGetProperty("type", out JsonElement type) && type.GetString() == "kycShareNeeded")
		{
			return ParseKycShareNeeded(blocker);
		}

		if (blocker.TryGetProperty("code", out JsonElement code)
			&& code.GetString() == KycShareNeededCode)
		{
			return ParseKycShareFromErrorEnvelope(blocker);
		}

		return null;
	}

	public static UserActionNeededBlocker? TryParseUserActionNeeded(JsonElement blocker)
	{
		if (blocker.TryGetProperty("type", out JsonElement type) && type.GetString() == "userActionNeeded")
		{
			return ParseUserActionNeeded(blocker);
		}

		if (blocker.TryGetProperty("code", out JsonElement code)
			&& code.GetString() == UserActionNeededCode)
		{
			return ParseUserActionFromErrorEnvelope(blocker);
		}

		return null;
	}

	private static KycShareNeededBlocker ParseKycShareNeeded(JsonElement blocker)
	{
		JsonElement? tosFlow = blocker.TryGetProperty("tosFlow", out JsonElement tos) && tos.ValueKind != JsonValueKind.Null
			? tos
			: null;

		IReadOnlyList<string>? neededAttributes = blocker.TryGetProperty("neededAttributes", out JsonElement attributes)
			&& attributes.ValueKind == JsonValueKind.Array
			? attributes.EnumerateArray()
				.Select(element => element.GetString())
				.Where(value => value is not null)
				.Select(value => value!)
				.ToArray()
			: null;

		IReadOnlyList<string> shareWithPrincipals = blocker.TryGetProperty("shareWithPrincipals", out JsonElement principals)
			&& principals.ValueKind == JsonValueKind.Array
			? principals.EnumerateArray()
				.Select(element => element.GetString())
				.Where(value => value is not null)
				.Select(value => value!)
				.ToArray()
			: Array.Empty<string>();

		JsonElement acceptedIssuers = blocker.TryGetProperty("acceptedIssuers", out JsonElement issuers)
			? issuers
			: default;

		return new KycShareNeededBlocker(tosFlow, neededAttributes, shareWithPrincipals, acceptedIssuers);
	}

	private static KycShareNeededBlocker ParseKycShareFromErrorEnvelope(JsonElement envelope)
	{
		if (!envelope.TryGetProperty("data", out JsonElement data))
		{
			throw new InvalidOperationException("KYC share blocker is missing data");
		}

		return ParseKycShareNeeded(data);
	}

	private static UserActionNeededBlocker ParseUserActionNeeded(JsonElement blocker)
	{
		JsonElement[] actions = blocker.TryGetProperty("actionsNeeded", out JsonElement actionsNeeded)
			&& actionsNeeded.ValueKind == JsonValueKind.Array
			? actionsNeeded.EnumerateArray().ToArray()
			: Array.Empty<JsonElement>();

		return new UserActionNeededBlocker(actions);
	}

	private static UserActionNeededBlocker ParseUserActionFromErrorEnvelope(JsonElement envelope)
	{
		if (!envelope.TryGetProperty("data", out JsonElement data))
		{
			throw new InvalidOperationException("User action blocker is missing data");
		}

		return ParseUserActionNeeded(data);
	}
}

public sealed record KycShareNeededBlocker(
	JsonElement? TosFlow,
	IReadOnlyList<string>? NeededAttributes,
	IReadOnlyList<string> ShareWithPrincipals,
	JsonElement AcceptedIssuers)
{
	public bool RequiresTrustedChain =>
		AcceptedIssuers.ValueKind == JsonValueKind.Array && AcceptedIssuers.GetArrayLength() > 0;
}

public sealed record UserActionNeededBlocker(IReadOnlyList<JsonElement> ActionsNeeded);
