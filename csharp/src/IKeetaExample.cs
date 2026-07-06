namespace KeetaNet.Examples;

/// <summary>One runnable port of a keetanet-examples TypeScript sample.</summary>
public interface IKeetaExample
{
	/// <summary>Example id matching the TypeScript path without extension (e.g. <c>anchor/kyc-client</c>).</summary>
	string Id { get; }

	/// <summary>Human-readable description from the TypeScript file header.</summary>
	string Description { get; }

	Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default);
}
