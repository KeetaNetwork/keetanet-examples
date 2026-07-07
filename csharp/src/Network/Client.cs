using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using KeetaNet.Examples.Common;

namespace KeetaNet.Examples.Network;

public sealed class Client
{
	private static readonly HttpClient Http = new();

	private readonly string _apiUrl;

	public Client(string apiUrl) => _apiUrl = apiUrl.TrimEnd('/');

	public static Client FromNetwork(string network) => network switch
	{
		"test" => new Client(Constants.NodeApi),
		_ => throw new ArgumentException($"Unsupported network: {network}", nameof(network)),
	};

	public async Task<BigInteger> GetBalanceAsync(
		string account,
		string token,
		CancellationToken cancellationToken = default)
	{
		using HttpResponseMessage response = await Http.GetAsync(
			$"{_apiUrl}/node/ledger/account/{account}/balance/{token}",
			cancellationToken).ConfigureAwait(false);
		response.EnsureSuccessStatusCode();

		await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
		BalanceJson? payload = await JsonSerializer.DeserializeAsync<BalanceJson>(
			stream,
			cancellationToken: cancellationToken).ConfigureAwait(false);

		return ParseBalance(payload?.Balance ?? "0");
	}

	public async Task<IReadOnlyList<TokenBalance>> GetAllBalancesAsync(
		string account,
		CancellationToken cancellationToken = default)
	{
		using HttpResponseMessage response = await Http.GetAsync(
			$"{_apiUrl}/node/ledger/account/{account}/balance",
			cancellationToken).ConfigureAwait(false);
		response.EnsureSuccessStatusCode();

		await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
		BalanceListJson? payload = await JsonSerializer.DeserializeAsync<BalanceListJson>(
			stream,
			cancellationToken: cancellationToken).ConfigureAwait(false);

		if (payload?.Balances is null)
		{
			return Array.Empty<TokenBalance>();
		}

		return payload.Balances
			.Select(entry => new TokenBalance(entry.Token, ParseBalance(entry.Balance)))
			.ToArray();
	}

	// TODO: Remove this once it's added to the KYCClient
	public async Task<IReadOnlyList<OnChainCertificate>> GetAllCertificatesAsync(
		string account,
		CancellationToken cancellationToken = default)
	{
		using HttpResponseMessage response = await Http.GetAsync(
			$"{_apiUrl}/node/ledger/account/{account}/certificates",
			cancellationToken).ConfigureAwait(false);
		response.EnsureSuccessStatusCode();

		await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
		CertificateListJson? payload = await JsonSerializer.DeserializeAsync<CertificateListJson>(
			stream,
			cancellationToken: cancellationToken).ConfigureAwait(false);

		if (payload?.Certificates is null)
		{
			return Array.Empty<OnChainCertificate>();
		}

		return payload.Certificates
			.Select(entry => new OnChainCertificate(entry.Certificate, entry.Intermediates))
			.ToArray();
	}

	internal static BigInteger ParseBalance(string balance)
	{
		if (!balance.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
		{
			return BigInteger.Parse(balance, NumberStyles.Integer, CultureInfo.InvariantCulture);
		}

		string hex = balance[2..];
		if (hex.Length == 0)
		{
			return BigInteger.Zero;
		}

		if (hex.Length % 2 != 0)
		{
			hex = "0" + hex;
		}

		byte[] bytes = Convert.FromHexString(hex);
		return new BigInteger(bytes, isUnsigned: true, isBigEndian: true);
	}

	private sealed record BalanceJson([property: JsonPropertyName("balance")] string Balance);

	private sealed record BalanceListJson([property: JsonPropertyName("balances")] BalanceEntryJson[] Balances);

	private sealed record BalanceEntryJson(
		[property: JsonPropertyName("token")] string Token,
		[property: JsonPropertyName("balance")] string Balance);

	private sealed record CertificateListJson(
		[property: JsonPropertyName("certificates")] CertificateEntryJson[] Certificates);

	private sealed record CertificateEntryJson(
		[property: JsonPropertyName("certificate")] string Certificate,
		[property: JsonPropertyName("intermediates")] string[]? Intermediates);
}
