using System.Globalization;
using System.Numerics;
using KeetaNet.Anchor;
using KeetaNet.Anchor.Crypto;
using KeetaNet.Examples.Common;
using KeetaNet.Examples.Ledger;
using CryptoCertificate = KeetaNet.Anchor.Crypto.Certificate;

namespace KeetaNet.Examples.Network;

public sealed class UserClient
{
	private UserClient(Account? signer, string networkAddress, string baseToken, ulong network)
	{
		Signer = signer;
		NetworkAddress = networkAddress;
		BaseToken = baseToken;
		Network = network;
	}

	public Account? Signer { get; }

	public string NetworkAddress { get; }

	public string BaseToken { get; }

	public ulong Network { get; }

	public static UserClient FromNetwork(string network, Account? signer) =>
		network switch
		{
			"test" => new UserClient(
				signer,
				Constants.MetadataRoot,
				Constants.BaseTokenAddress,
				Constants.NetworkId),
			_ => throw new ArgumentException($"Unsupported network: {network}", nameof(network)),
		};

	public Task<string> Send(
		WasmRuntime runtime,
		Account to,
		BigInteger amount,
		Account token,
		string external,
		CancellationToken cancellationToken = default)
	{
		Account signer = RequireSigner();
		return Task.Run(
			() => LedgerPublisher.PublishSend(
				runtime,
				signer,
				to,
				amount.ToString(CultureInfo.InvariantCulture),
				token,
				external,
				Network,
				cancellationToken),
			cancellationToken);
	}

	public Task<string> ModifyCertificate(
		WasmRuntime runtime,
		string certificatePem,
		IReadOnlyList<string>? intermediatePems,
		Account? blockAccount = null,
		CancellationToken cancellationToken = default)
	{
		Account signer = RequireSigner();
		Account account = blockAccount ?? signer;
		return Task.Run(
			() =>
			{
				using CryptoCertificate certificate = runtime.Certificates.Parse(certificatePem);
				string certificateDerHex = OperationFactory.ToDerHex(certificate);
				List<string> intermediateDerHex = new();
				if (intermediatePems is not null)
				{
					foreach (string intermediatePem in intermediatePems)
					{
						using CryptoCertificate intermediate = runtime.Certificates.Parse(intermediatePem);
						intermediateDerHex.Add(OperationFactory.ToDerHex(intermediate));
					}
				}

				using Operation operation = runtime.Operations().ManageCertificateAdd(certificateDerHex, intermediateDerHex);
				return LedgerPublisher.PublishOperations(
					runtime,
					account,
					signer,
					new[] { operation },
					Network,
					cancellationToken);
			},
			cancellationToken);
	}

	public Task<string> PublishOperations(
		WasmRuntime runtime,
		IReadOnlyList<Operation> operations,
		Account? blockAccount = null,
		CancellationToken cancellationToken = default)
	{
		Account signer = RequireSigner();
		Account account = blockAccount ?? signer;
		return Task.Run(
			() => LedgerPublisher.PublishOperations(runtime, account, signer, operations, Network, cancellationToken),
			cancellationToken);
	}

	private Account RequireSigner() =>
		Signer ?? throw new InvalidOperationException("No signer available in a read-only UserClient");
}
