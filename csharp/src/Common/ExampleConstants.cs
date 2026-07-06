namespace KeetaNet.Examples.Common;

/// <summary>Shared testnet constants from keetanet-examples anchor samples.</summary>
public static class ExampleConstants
{
	public const string FootprintProviderId = "Footprint";

	public const string KeetaUsdcAsset = "keeta_apna75yhhvnv4ei7ape55hndk4yepno7a7i2mhtiwahiygixjcnmvswxhnmnk";

	public const string KeetaUsdAsset = "keeta_any4zllibya6fum3lsoimxmnmeo57nklxlh4c6d6xosfacarfaa3knkiprkmm";

	public const string BankAccountUsLocation = "bank-account:us";

	public const string BaseSepoliaLocation = "chain:evm:84532";

	public const string KeetaTestNetworkLocation = "chain:keeta:1413829460";

	public const string Dev2ProviderId = "DEV2";

	/// <summary>Keeta Test Network KYC Root CA — used to verify on-chain KYC certificate chains.</summary>
	public const string KycRootCaPem = """
		-----BEGIN CERTIFICATE-----
		MIIBiDCCAS2gAwIBAgIGAZhi7awAMAsGCWCGSAFlAwQDCjApMScwJQYDVQQDEx5L
		ZWV0YSBUZXN0IE5ldHdvcmsgS1lDIFJvb3QgQ0EwHhcNMjUwODAxMDAwMDAwWhcN
		MjgwODAxMDAwMDAwWjApMScwJQYDVQQDEx5LZWV0YSBUZXN0IE5ldHdvcmsgS1lD
		IFJvb3QgQ0EwNjAQBgcqhkjOPQIBBgUrgQQACgMiAAKK1O9NiYvu2sBYNRPfjOpp
		sNSMZ1lOVn+psFdk3Ugq2qNjMGEwDwYDVR0TAQH/BAUwAwEB/zAOBgNVHQ8BAf8E
		BAMCAMYwHwYDVR0jBBgwFoAUap82oKFjJ2jhIj2CGABULiX4h3owHQYDVR0OBBYE
		FGqfNqChYydo4SI9ghgAVC4l+Id6MAsGCWCGSAFlAwQDCgNIADBFAiEAqnl85S6v
		bw8HLO+YXhnwqq6GmnY+7tCcnwYtoyDzYTMCIEw7ALqHJp0kO9AExm5sSoC7rPOd
		GlX42GsZQW3AJ7Jc
		-----END CERTIFICATE-----
		""";
}
