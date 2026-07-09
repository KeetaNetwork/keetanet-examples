using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using KeetaNet.Anchor;
using KeetaNet.Anchor.Crypto;
using KeetaNet.Examples.Common;

namespace KeetaNet.Examples.Ledger;

/// <summary>Extension entry points matching the anchor-csharp factory pattern.</summary>
public static class WasmRuntimeExtensions
{
	public static OperationFactory Operations(this WasmRuntime runtime) => new(runtime);

	public static BlockBuilderFactory Blocks(this WasmRuntime runtime) => new(runtime);
}

/// <summary>Builds, signs, and publishes blocks over the node HTTP API.</summary>
public static class LedgerPublisher
{
	public static string PublishSend(
		WasmRuntime runtime,
		Account signer,
		Account to,
		string amount,
		Account token,
		string external,
		ulong networkId,
		CancellationToken cancellationToken)
	{
		using Operation send = runtime.Operations().Send(to, amount, token, external);
		using SignedBlock block = BlockSealer.BuildSigned(runtime, signer, signer, networkId, send);
		Transmit(runtime, signer, new[] { block }, cancellationToken);
		return block.HashHex;
	}

	public static string PublishOperations(
		WasmRuntime runtime,
		Account blockAccount,
		Account signer,
		IReadOnlyList<Operation> operations,
		ulong networkId,
		CancellationToken cancellationToken)
	{
		using SignedBlock block = BlockSealer.BuildSigned(
			runtime,
			blockAccount,
			signer,
			networkId,
			operations,
			cancellationToken: cancellationToken);
		Transmit(runtime, signer, new[] { block }, cancellationToken);
		return block.HashHex;
	}

	internal static string? GetHeadHash(WasmRuntime runtime, Account account, CancellationToken cancellationToken)
	{
		using NodeClient nodeClient = runtime.CreateNodeClient(Constants.NodeApi);
		AccountState state = nodeClient.GetAccountState(account, cancellationToken).GetAwaiter().GetResult();
		return state.HeadBlock?.ToString();
	}

	private static void Transmit(
		WasmRuntime runtime,
		Account feeSigner,
		IReadOnlyList<SignedBlock> blocks,
		CancellationToken cancellationToken)
	{
		List<string> encoded = blocks.Select(block => Convert.ToBase64String(block.ToBytes())).ToList();
		IReadOnlyList<string> temporary = RequestVotes(encoded, priorVotes: null, cancellationToken);

		SignedBlock? feeBlock = BuildFeeBlock(runtime, feeSigner, blocks, temporary[0], cancellationToken);
		try
		{
			List<SignedBlock> all = blocks.ToList();
			List<string> encodedAll = encoded;
			if (feeBlock is not null)
			{
				all.Add(feeBlock);
				encodedAll = all.Select(block => Convert.ToBase64String(block.ToBytes())).ToList();
			}

			IReadOnlyList<string> permanent = RequestVotes(encodedAll, temporary, cancellationToken);
			PublishStaple(runtime, all, permanent, cancellationToken);
		}
		finally
		{
			feeBlock?.Dispose();
		}
	}

	private static SignedBlock? BuildFeeBlock(
		WasmRuntime runtime,
		Account feeSigner,
		IReadOnlyList<SignedBlock> blocks,
		string temporaryVoteBase64,
		CancellationToken cancellationToken)
	{
		byte[] voteBytes = Convert.FromBase64String(temporaryVoteBase64);
		using Vote vote = Vote.FromBytes(runtime, voteBytes);
		using Account baseToken = runtime.Accounts.FromPublicKeyString(Constants.BaseTokenAddress);
		using Operation? feeOperation = vote.CreateFeeSend(baseToken);
		if (feeOperation is null)
		{
			return null;
		}

		string? previous = blocks.LastOrDefault(block => block.AccountAddress == feeSigner.PublicKeyString)?.HashHex
			?? GetHeadHash(runtime, feeSigner, cancellationToken);

		return BlockSealer.BuildSigned(
			runtime,
			feeSigner,
			feeSigner,
			Constants.NetworkId,
			feeOperation,
			purpose: "fee",
			headHashHex: previous,
			cancellationToken);
	}

	private static void PublishStaple(
		WasmRuntime runtime,
		IReadOnlyList<SignedBlock> blocks,
		IReadOnlyList<string> permanentVoteBase64,
		CancellationToken cancellationToken)
	{
		List<Vote> votes = permanentVoteBase64
			.Select(encoded => Vote.FromBytes(runtime, Convert.FromBase64String(encoded)))
			.ToList();

		try
		{
			int[] voteHandles = votes.Select(vote => vote.RawHandle).ToArray();
			byte[] staple = GuestInterop.Run(runtime, () =>
			{
				using var args = new GuestInterop.ArgumentScope(runtime);
				int[] blockHandles = blocks.Select(block => block.RawHandle).ToArray();
				GuestInterop.Argument blocksArg = args.WriteHandles(blockHandles);
				GuestInterop.Argument votesArg = args.WriteHandles(voteHandles);
				int result = GuestInterop.Invoke(
					runtime,
					"keeta_vote_staple_build",
					blocksArg.Pointer,
					blockHandles.Length * 4,
					votesArg.Pointer,
					voteHandles.Length * 4,
					DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

				return GuestInterop.TakeBytes(runtime, result);
			});

			string stapleBase64 = Convert.ToBase64String(staple);

			using var request = new HttpRequestMessage(HttpMethod.Post, $"{Constants.NodeApi}/node/publish")
			{
				Content = new StringContent(
					JsonSerializer.Serialize(new PublishRequest(stapleBase64)),
					Encoding.UTF8,
					"application/json"),
			};

			using HttpResponseMessage response = Http.SendAsync(request, cancellationToken).GetAwaiter().GetResult();
			response.EnsureSuccessStatusCode();
		}
		finally
		{
			foreach (Vote vote in votes)
			{
				vote.Dispose();
			}
		}
	}

	private static IReadOnlyList<string> RequestVotes(
		IReadOnlyList<string> blocksBase64,
		IReadOnlyList<string>? priorVotes,
		CancellationToken cancellationToken)
	{
		List<string> votes = new();
		foreach (string api in Constants.RepresentativeApis)
		{
			try
			{
				string vote = RequestVote(api, blocksBase64, priorVotes, cancellationToken);
				votes.Add(vote);
			}
			catch (HttpRequestException)
			{
				// Individual reps may decline; quorum needs at least one successful vote.
			}
		}

		if (votes.Count == 0)
		{
			throw new InvalidOperationException("no representative returned a vote");
		}

		return votes;
	}

	private static string RequestVote(
		string apiBase,
		IReadOnlyList<string> blocksBase64,
		IReadOnlyList<string>? priorVotes,
		CancellationToken cancellationToken)
	{
		object body = priorVotes is null
			? new VoteRequest(blocksBase64)
			: new VoteRequest(blocksBase64, priorVotes);

		using var request = new HttpRequestMessage(HttpMethod.Post, $"{apiBase.TrimEnd('/')}/vote")
		{
			Content = new StringContent(JsonSerializer.Serialize(body, VoteJsonOptions), Encoding.UTF8, "application/json"),
		};

		using HttpResponseMessage response = Http.SendAsync(request, cancellationToken).GetAwaiter().GetResult();
		if (!response.IsSuccessStatusCode)
		{
			string errorBody = response.Content.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
			throw new HttpRequestException(
				$"Vote request failed with {(int)response.StatusCode} {response.ReasonPhrase}: {errorBody}");
		}

		using JsonDocument document = JsonDocument.Parse(response.Content.ReadAsStreamAsync(cancellationToken).GetAwaiter().GetResult());
		string? vote = document.RootElement.GetProperty("vote").GetProperty("$binary").GetString();
		if (string.IsNullOrEmpty(vote))
		{
			throw new InvalidOperationException("node returned no vote");
		}

		return vote;
	}

	private static readonly JsonSerializerOptions VoteJsonOptions = new()
	{
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
	};

	private static readonly HttpClient Http = new();

	private sealed record VoteRequest(
		[property: JsonPropertyName("blocks")] IReadOnlyList<string> Blocks,
		[property: JsonPropertyName("votes")] IReadOnlyList<string>? Votes = null);

	private sealed record PublishRequest(
		[property: JsonPropertyName("votesAndBlocks")] string VotesAndBlocks);
}

/// <summary>Seals one operation into a signed block for an account.</summary>
internal static class BlockSealer
{
	internal static SignedBlock BuildSigned(
		WasmRuntime runtime,
		Account blockAccount,
		Account signer,
		ulong networkId,
		Operation operation,
		string? purpose = null,
		string? headHashHex = null,
		CancellationToken cancellationToken = default) =>
		BuildSigned(
			runtime,
			blockAccount,
			signer,
			networkId,
			new[] { operation },
			purpose,
			headHashHex,
			cancellationToken);

	internal static SignedBlock BuildSigned(
		WasmRuntime runtime,
		Account blockAccount,
		Account signer,
		ulong networkId,
		IReadOnlyList<Operation> operations,
		string? purpose = null,
		string? headHashHex = null,
		CancellationToken cancellationToken = default)
	{
		string? head = headHashHex ?? LedgerPublisher.GetHeadHash(runtime, blockAccount, cancellationToken);
		BlockBuilder builder = runtime.Blocks().Builder()
			.Version(2)
			.Network(networkId)
			.Account(blockAccount)
			.Signer(signer)
			.Date(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

		if (!string.IsNullOrWhiteSpace(purpose))
		{
			builder = builder.Purpose(purpose);
		}

		builder = string.IsNullOrWhiteSpace(head)
			? builder.Opening()
			: builder.Previous(Convert.FromHexString(head));

		foreach (Operation operation in operations)
		{
			builder.AddOperation(operation);
		}

		using (builder)
		{
			using UnsignedBlock unsigned = builder.Build();
			return unsigned.Sign();
		}
	}
}
