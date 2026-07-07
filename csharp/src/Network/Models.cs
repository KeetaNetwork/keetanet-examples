using System.Numerics;

namespace KeetaNet.Examples.Network;

public sealed record TokenBalance(string Token, BigInteger Balance);

/// <summary>One on-chain certificate and its intermediate chain, as returned by the node API.</summary>
public sealed record OnChainCertificate(string CertificatePem, IReadOnlyList<string>? IntermediatePems);
