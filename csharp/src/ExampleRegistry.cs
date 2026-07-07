namespace KeetaNet.Examples;

/// <summary>Registers C# ports of keetanet-examples TypeScript samples.</summary>
public static class ExampleRegistry
{
	private static readonly IReadOnlyList<IKeetaExample> All = BuildExamples();

	public static IReadOnlyList<IKeetaExample> Examples => All;

	public static IKeetaExample? Find(string id) =>
		All.FirstOrDefault(example => example.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

	private static IReadOnlyList<IKeetaExample> BuildExamples() =>
	[
		new Anchor.KycClientExample(),
		new Anchor.KycClientShareKycExample(),
		new Anchor.AssetMovementEvmInboundExample(),
		new Anchor.AssetMovementEvmOutboundExample(),
		new Anchor.AssetMovementFiatDepositFromCryptoExample(),
		new Anchor.AssetMovementFiatDepositFromBankExample(),
	];
}
