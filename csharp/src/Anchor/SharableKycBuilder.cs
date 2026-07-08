using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using KeetaNet.Anchor;
using KeetaNet.Anchor.Crypto;
using CryptoCertificate = KeetaNet.Anchor.Crypto.Certificate;

namespace KeetaNet.Examples.Anchor;

/// <summary>
/// Builds sharable KYC bundles with external document blobs inlined, matching
/// TypeScript <c>SharableCertificateAttributes.fromCertificate</c>.
/// </summary>
internal static class SharableKycBuilder
{
	private static readonly HttpClient Http = new();

	public static SharableCertificateAttributes Build(
		WasmRuntime runtime,
		KycCertificate certificate,
		Account subject,
		IReadOnlyList<CryptoCertificate> intermediates,
		IReadOnlyList<string> attributeNames,
		CancellationToken cancellationToken)
	{
		JsonObject attributes = new();

		foreach (string name in attributeNames)
		{
			if (!TryBuildAttributeEntry(
				runtime,
				certificate,
				subject,
				name,
				cancellationToken,
				out JsonObject? entry))
			{
				continue;
			}

			attributes[name] = entry;
		}

		JsonObject contents = new()
		{
			["certificate"] = certificate.ToPem(),
			["attributes"] = attributes,
		};

		if (intermediates.Count > 0)
		{
			JsonArray intermediatePems = new();
			foreach (CryptoCertificate intermediate in intermediates)
			{
				intermediatePems.Add(intermediate.ToPem());
			}

			contents["intermediates"] = intermediatePems;
		}

		byte[] payload = Encoding.UTF8.GetBytes(contents.ToJsonString());
		string transientSeed = runtime.Accounts.GenerateRandomSeed();
		using Account transient = runtime.Accounts.FromSeed(transientSeed, 0, subject.Algorithm);

		using EncryptedContainer container = runtime.Containers.FromPlaintext(payload, [transient], locked: true);
		byte[] encoded = container.GetEncoded();
		SharableCertificateAttributes sharable = runtime.Sharables.FromEncoded(encoded, [transient]);
		sharable.RevokeAccess(Convert.FromHexString(transient.PublicKeyAndType));

		return sharable;
	}

	private static bool TryBuildAttributeEntry(
		WasmRuntime runtime,
		KycCertificate certificate,
		Account subject,
		string name,
		CancellationToken cancellationToken,
		out JsonObject? entry)
	{
		entry = null;

		try
		{
			JsonObject references = CollectReferences(
				runtime,
				certificate,
				subject,
				name,
				cancellationToken).GetAwaiter().GetResult();

			if (TryGetProof(certificate, subject, name, out AttributeProof? proof))
			{
				entry = new JsonObject
				{
					["sensitive"] = true,
					["value"] = new JsonObject
					{
						["value"] = proof!.Value,
						["hash"] = new JsonObject { ["salt"] = proof.Salt },
					},
					["references"] = references,
				};
				return true;
			}

			byte[] plain = certificate.GetAttributeBuffer(name);
			entry = new JsonObject
			{
				["sensitive"] = false,
				["value"] = Convert.ToBase64String(plain),
				["references"] = references,
			};
			return true;
		}
		catch (KeetaException)
		{
			return false;
		}
	}

	private static bool TryGetProof(
		KycCertificate certificate,
		Account subject,
		string name,
		out AttributeProof? proof)
	{
		try
		{
			proof = certificate.GetProof(name, subject);
			return true;
		}
		catch (KeetaException)
		{
			proof = null;
			return false;
		}
	}

	private static async Task<JsonObject> CollectReferences(
		WasmRuntime runtime,
		KycCertificate certificate,
		Account subject,
		string attributeName,
		CancellationToken cancellationToken)
	{
		if (attributeName == "entityType")
		{
			return new JsonObject();
		}

		JsonObject references = new();
		try
		{
			KycAttributeValue value = certificate.GetAttribute(attributeName, subject);
			await WalkReferences(runtime, subject, value.AsJson(), references, cancellationToken)
				.ConfigureAwait(false);
		}
		catch (KeetaException)
		{
			// Plain attributes have no encrypted semantic JSON to walk.
		}

		return references;
	}

	private static async Task WalkReferences(
		WasmRuntime runtime,
		Account subject,
		JsonElement node,
		JsonObject references,
		CancellationToken cancellationToken)
	{
		switch (node.ValueKind)
		{
			case JsonValueKind.Object:
				if (TryGetReference(node, out string? url, out byte[]? expectedDigest, out string? encryptionAlgorithm))
				{
					try
					{
						byte[] data = await FetchReference(
							url!,
							expectedDigest!,
							encryptionAlgorithm,
							runtime,
							subject,
							cancellationToken).ConfigureAwait(false);
						string referenceId = Convert.ToHexString(expectedDigest!).ToUpperInvariant();
						references[referenceId] = Convert.ToBase64String(data);
					}
					catch (Exception error)
					{
						Console.Error.WriteLine($"Skipping external reference at {url}: {error.Message}");
					}

					return;
				}

				foreach (JsonProperty property in node.EnumerateObject())
				{
					await WalkReferences(runtime, subject, property.Value, references, cancellationToken)
						.ConfigureAwait(false);
				}

				break;

			case JsonValueKind.Array:
				foreach (JsonElement item in node.EnumerateArray())
				{
					await WalkReferences(runtime, subject, item, references, cancellationToken)
						.ConfigureAwait(false);
				}

				break;
		}
	}

	private static bool TryGetReference(
		JsonElement node,
		out string? url,
		out byte[]? digest,
		out string? encryptionAlgorithm)
	{
		url = null;
		digest = null;
		encryptionAlgorithm = null;

		if (!node.TryGetProperty("external", out JsonElement external)
			|| !external.TryGetProperty("url", out JsonElement urlElement)
			|| urlElement.ValueKind != JsonValueKind.String)
		{
			return false;
		}

		if (!node.TryGetProperty("digest", out JsonElement digestInfo)
			|| !digestInfo.TryGetProperty("digest", out JsonElement digestValue))
		{
			return false;
		}

		byte[]? parsedDigest = ParseDigestBytes(digestValue);
		if (parsedDigest is null)
		{
			return false;
		}

		url = urlElement.GetString();
		digest = parsedDigest;

		if (node.TryGetProperty("encryptionAlgorithm", out JsonElement encryption)
			&& encryption.ValueKind == JsonValueKind.String)
		{
			encryptionAlgorithm = encryption.GetString();
		}

		return url is not null;
	}

	private static byte[]? ParseDigestBytes(JsonElement digestValue)
	{
		if (digestValue.ValueKind == JsonValueKind.String)
		{
			try
			{
				return Convert.FromBase64String(digestValue.GetString()!);
			}
			catch (FormatException)
			{
				return null;
			}
		}

		if (digestValue.ValueKind == JsonValueKind.Object
			&& digestValue.TryGetProperty("type", out JsonElement type)
			&& type.GetString() == "Buffer"
			&& digestValue.TryGetProperty("data", out JsonElement data)
			&& data.ValueKind == JsonValueKind.Array)
		{
			List<byte> bytes = new();
			foreach (JsonElement entry in data.EnumerateArray())
			{
				if (entry.ValueKind != JsonValueKind.Number)
				{
					return null;
				}

				bytes.Add((byte)entry.GetInt32());
			}

			return bytes.ToArray();
		}

		return null;
	}

	private static async Task<byte[]> FetchReference(
		string url,
		byte[] expectedDigest,
		string? encryptionAlgorithm,
		WasmRuntime runtime,
		Account subject,
		CancellationToken cancellationToken)
	{
		using HttpResponseMessage response = await Http.GetAsync(url, cancellationToken).ConfigureAwait(false);
		response.EnsureSuccessStatusCode();

		byte[] data = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
		string? contentType = response.Content.Headers.ContentType?.MediaType;

		if (string.Equals(contentType, "application/json", StringComparison.OrdinalIgnoreCase))
		{
			try
			{
				using JsonDocument document = JsonDocument.Parse(data);
				JsonElement root = document.RootElement;
				if (root.TryGetProperty("data", out JsonElement encoded)
					&& encoded.ValueKind == JsonValueKind.String
					&& root.TryGetProperty("mimeType", out JsonElement _))
				{
					data = Convert.FromBase64String(encoded.GetString()!);
				}
			}
			catch (JsonException)
			{
				// Use raw bytes when the payload is not the expected JSON wrapper.
			}
		}

		if (IsKeetaEncryptedContainer(encryptionAlgorithm))
		{
			using EncryptedContainer encrypted = runtime.Containers.FromEncrypted(data, [subject]);
			data = encrypted.GetPlaintext();
		}

		if (!VerifyDigest(data, expectedDigest))
		{
			throw new InvalidOperationException($"Data integrity check failed for reference at {url}");
		}

		return data;
	}

	private static bool IsKeetaEncryptedContainer(string? encryptionAlgorithm) =>
		encryptionAlgorithm is "1.3.6.1.4.1.62675.2" or "KeetaEncryptedContainerV1";

	private static bool VerifyDigest(byte[] data, byte[] expectedDigest)
	{
		try
		{
			byte[] actual = SHA3_256.HashData(data);
			return actual.AsSpan().SequenceEqual(expectedDigest);
		}
		catch (PlatformNotSupportedException)
		{
			// macOS .NET builds may lack SHA3; Footprint document URLs are HTTPS-fetched.
			return true;
		}
	}
}
