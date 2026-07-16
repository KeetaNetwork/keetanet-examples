namespace KeetaNet.Examples;

internal static class Program
{
	public static async Task<int> Main(string[] args)
	{
		if (args.Length == 0 || args[0] is "--help" or "-h" or "help")
		{
			PrintHelp();
			return 0;
		}

		string exampleId = args[0];
		string[] exampleArgs = args.Skip(1).ToArray();

		IKeetaExample? example = ExampleRegistry.Find(exampleId);
		if (example is null)
		{
			Console.Error.WriteLine($"Unknown example: {exampleId}");
			Console.Error.WriteLine("Run without arguments to list available examples.");
			return 1;
		}

		try
		{
			return await example.Run(exampleArgs).ConfigureAwait(false);
		}
		catch (Exception error)
		{
			Console.Error.WriteLine(error);
			return 1;
		}
	}

	private static void PrintHelp()
	{
		Console.WriteLine("Keeta Network Anchor SDK Examples (C#)");
		Console.WriteLine("======================================");
		Console.WriteLine();
		Console.WriteLine("Usage: dotnet run --project src/KeetaNet.Examples -- <example-id> [args...]");
		Console.WriteLine("       make <example-id>");
		Console.WriteLine();
		Console.WriteLine("Examples:");

		foreach (IKeetaExample example in ExampleRegistry.Examples.OrderBy(entry => entry.Id))
		{
			Console.WriteLine($"      {example.Id,-46} - {example.Description}");
		}
	}
}
