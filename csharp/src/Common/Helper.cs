using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using KeetaNet.Anchor.Crypto;
using KeetaNet.Examples.Network;

namespace KeetaNet.Examples.Common;

public static class Helper
{
	public static string ReadLine(string prompt)
	{
		Console.Write(prompt);
		return Console.In.ReadLine() ?? string.Empty;
	}

	public static async Task<bool> WaitForResultAsync(
		Func<Task<bool>> predicate,
		TimeSpan? timeout = null,
		CancellationToken cancellationToken = default)
	{
		DateTimeOffset deadline = DateTimeOffset.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
		while (DateTimeOffset.UtcNow < deadline)
		{
			if (await predicate().ConfigureAwait(false))
			{
				return true;
			}

			await Task.Delay(50, cancellationToken).ConfigureAwait(false);
		}

		return false;
	}

	public static async Task<bool> GetFaucetTokensAsync(
		Account account,
		string network,
		CancellationToken cancellationToken = default)
	{
		if (!string.Equals(network, "test", StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException("Faucet is only available on the test network");
		}

		Client client = Client.FromNetwork(network);
		BigInteger initial = await client.GetBalanceAsync(account.Address, Constants.BaseTokenAddress, cancellationToken)
			.ConfigureAwait(false);
		BigInteger expectedCredit = BigInteger.Pow(10, 9) * 5;

		try
		{
			using var request = new HttpRequestMessage(HttpMethod.Post, Constants.FaucetUrl)
			{
				Content = new FormUrlEncodedContent(new Dictionary<string, string>
				{
					["address"] = account.Address,
					["amount"] = "5",
				}),
			};

			using var http = new HttpClient();
			using HttpResponseMessage response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
			string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

			if (response.IsSuccessStatusCode && body.Contains("Sent ", StringComparison.OrdinalIgnoreCase))
			{
				Console.WriteLine($"Requesting tokens from faucet for: {account.Address}");
			}
			else
			{
				Console.Error.WriteLine(
					$"Faucet request failed for: {account.Address} (HTTP {(int)response.StatusCode})");
			}
		}
		catch (Exception error)
		{
			Console.Error.WriteLine($"Faucet request failed for: {account.Address} {error.Message}");
		}

		return await WaitForResultAsync(async () =>
		{
			try
			{
				BigInteger current = await client.GetBalanceAsync(account.Address, Constants.BaseTokenAddress, cancellationToken)
					.ConfigureAwait(false);
				return current >= initial + expectedCredit;
			}
			catch (HttpRequestException)
			{
				return false;
			}
		}, timeout: TimeSpan.FromSeconds(60), cancellationToken: cancellationToken).ConfigureAwait(false);
	}

	public static string FormatDecimals(BigInteger amount, int decimals)
	{
		BigInteger scale = BigInteger.Pow(10, decimals);
		BigInteger whole = BigInteger.DivRem(amount, scale, out BigInteger fraction);
		string fractionText = fraction.ToString(CultureInfo.InvariantCulture).PadLeft(decimals, '0').TrimEnd('0');
		return fractionText.Length == 0 ? whole.ToString(CultureInfo.InvariantCulture) : $"{whole}.{fractionText}";
	}

	/// <summary>Port of <c>getTokenDecimals</c> from <c>src/helper.ts</c>.</summary>
	public static async Task<int?> GetTokenDecimalsAsync(
		string network,
		string? token = null,
		CancellationToken cancellationToken = default)
	{
		string apiUrl = network switch
		{
			"test" => Constants.NodeApi,
			_ => throw new ArgumentException($"Unsupported network: {network}", nameof(network)),
		};

		string tokenAddress = token ?? Constants.BaseTokenAddress;

		using HttpResponseMessage response = await Http.GetAsync(
			$"{apiUrl.TrimEnd('/')}/node/ledger/account/{tokenAddress}",
			cancellationToken).ConfigureAwait(false);
		response.EnsureSuccessStatusCode();

		await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
		AccountInfoJson? payload = await JsonSerializer.DeserializeAsync<AccountInfoJson>(
			stream,
			cancellationToken: cancellationToken).ConfigureAwait(false);

		if (payload?.Info?.Metadata is not { Length: > 0 } metadataBase64)
		{
			return null;
		}

		byte[] metadataBytes = Convert.FromBase64String(metadataBase64);
		byte[] decoded = TryInflateMetadata(metadataBytes);
		using JsonDocument document = JsonDocument.Parse(Encoding.UTF8.GetString(decoded));
		JsonElement root = document.RootElement;

		if (root.TryGetProperty("decimalPlaces", out JsonElement decimalPlaces))
		{
			return decimalPlaces.ValueKind switch
			{
				JsonValueKind.Number when decimalPlaces.TryGetInt32(out int value) => value,
				JsonValueKind.String when int.TryParse(
					decimalPlaces.GetString(),
					NumberStyles.Integer,
					CultureInfo.InvariantCulture,
					out int parsed) => parsed,
				_ => null,
			};
		}

		return null;
	}

	private static readonly HttpClient Http = new();

	private static byte[] TryInflateMetadata(byte[] data)
	{
		try
		{
			using var input = new MemoryStream(data);
			using var deflate = new ZLibStream(input, CompressionMode.Decompress);
			using var output = new MemoryStream();
			deflate.CopyTo(output);
			return output.ToArray();
		}
		catch (InvalidDataException)
		{
			return data;
		}
		catch (IOException)
		{
			return data;
		}
	}

	private sealed record AccountInfoJson([property: JsonPropertyName("info")] AccountMetadataJson? Info);

	private sealed record AccountMetadataJson([property: JsonPropertyName("metadata")] string? Metadata);
}
