namespace KeetaNet.Examples.Network;

/// <summary>One on-chain certificate and its intermediate chain, as returned by the node API.</summary>
public sealed record OnChainCertificate(string CertificatePem, IReadOnlyList<string>? IntermediatePems);
