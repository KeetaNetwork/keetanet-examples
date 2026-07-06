namespace KeetaNet.Examples.Common;

public static class TestnetEndpoints
{
	public const string NodeApi = "https://rep1.test.network.api.keeta.com/api";

	/// <summary>Every testnet representative API (quorum voting fans out to all four).</summary>
	public static readonly string[] RepresentativeApis =
	[
		"https://rep1.test.network.api.keeta.com/api",
		"https://rep2.test.network.api.keeta.com/api",
		"https://rep3.test.network.api.keeta.com/api",
		"https://rep4.test.network.api.keeta.com/api",
	];

	public const string FaucetUrl = "https://faucet.test.keeta.com";

	public const ulong NetworkId = 1413829460;

	public const string MetadataRoot = "keeta_aj5pgcaced3jjixdn7unsybr4bx2v2p22zyhwubggp3i7474dze3ehhc5b4u4";

	/// <summary>Native KTA base token on Keeta testnet (9 decimal places).</summary>
	public const string BaseTokenAddress = "keeta_anyiff4v34alvumupagmdyosydeq24lc4def5mrpmmyhx3j6vj2uucckeqn52";
}
