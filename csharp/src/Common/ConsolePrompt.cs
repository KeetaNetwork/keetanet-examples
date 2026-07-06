namespace KeetaNet.Examples.Common;

public static class ConsolePrompt
{
	public static string ReadLine(string prompt)
	{
		Console.Write(prompt);
		return System.Console.ReadLine() ?? string.Empty;
	}
}
