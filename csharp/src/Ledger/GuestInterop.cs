using System.Reflection;
using System.Text;
using KeetaNet.Anchor;
using Wasmtime;

namespace KeetaNet.Examples.Ledger;

/// <summary>
/// Bridges to anchor-csharp's private wasm runtime until ledger bindings ship in the package.
/// Delete this when <c>KeetaNet.Anchor</c> exposes public block/operation APIs.
/// </summary>
internal static class GuestInterop
{
	private static readonly MethodInfo RunMethod = GetGenericRunMethod();
	private static readonly MethodInfo TakeBytesMethod = GetMethod("TakeBytes");
	private static readonly MethodInfo TakeHandleMethod = GetMethod("TakeHandle");
	private static readonly MethodInfo RunFreeMethod = GetMethod("RunFree");
	private static readonly FieldInfo InstanceField = typeof(WasmRuntime).GetField("_instance", BindingFlags.Instance | BindingFlags.NonPublic)!;
	private static readonly FieldInfo MemoryField = typeof(WasmRuntime).GetField("_memory", BindingFlags.Instance | BindingFlags.NonPublic)!;
	private static readonly FieldInfo AllocField = typeof(WasmRuntime).GetField("_alloc", BindingFlags.Instance | BindingFlags.NonPublic)!;
	private static readonly FieldInfo DeallocField = typeof(WasmRuntime).GetField("_dealloc", BindingFlags.Instance | BindingFlags.NonPublic)!;

	public static T Run<T>(WasmRuntime runtime, Func<T> work)
	{
		MethodInfo run = RunMethod.MakeGenericMethod(typeof(T));
		return (T)run.Invoke(runtime, new object[] { work })!;
	}

	public static void Run(WasmRuntime runtime, Action work)
	{
		_ = RunMethod.MakeGenericMethod(typeof(object)).Invoke(runtime, new object[] { (Func<object>)(() => { work(); return null!; }) });
	}

	public static int AccountHandle(WasmObject obj)
	{
		PropertyInfo? handle = typeof(WasmObject).GetProperty("Handle", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
		return (int)handle!.GetValue(obj)!;
	}

	public static int Invoke(WasmRuntime runtime, string export, params object[] args)
	{
		MethodInfo invoke = typeof(WasmRuntime)
			.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
			.Single(method =>
				method.Name == "Invoke"
				&& method.IsGenericMethodDefinition
				&& method.GetParameters().Length == args.Length + 1
				&& method.GetGenericArguments().Length == args.Length + 1);

		Type[] typeArgs = args.Select(WasmArgumentType).Append(typeof(int)).ToArray();
		MethodInfo specialized = invoke.MakeGenericMethod(typeArgs);

		object?[] parameters = new object[args.Length + 1];
		parameters[0] = export;
		Array.Copy(args, 0, parameters, 1, args.Length);

		return (int)specialized.Invoke(runtime, parameters)!;
	}

	private static Type WasmArgumentType(object arg) =>
		arg switch
		{
			int => typeof(int),
			long => typeof(long),
			_ => throw new ArgumentException($"unsupported wasm argument type {arg.GetType().Name}", nameof(arg)),
		};

	public static byte[] TakeBytes(WasmRuntime runtime, int handle) =>
		(byte[])TakeBytesMethod.Invoke(runtime, new object[] { handle })!;

	public static int TakeHandle(WasmRuntime runtime, int handle) =>
		(int)TakeHandleMethod.Invoke(runtime, new object[] { handle })!;

	public static string TakeString(WasmRuntime runtime, int handle) =>
		Encoding.UTF8.GetString(TakeBytes(runtime, handle));

	public static void Free(WasmRuntime runtime, string export, int handle) =>
		RunFreeMethod.Invoke(runtime, new object[] { export, handle });

	private static MethodInfo GetMethod(string name) =>
		typeof(WasmRuntime).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
		?? throw new InvalidOperationException($"WasmRuntime.{name} not found");

	private static MethodInfo GetGenericRunMethod() =>
		typeof(WasmRuntime)
			.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
			.Single(method =>
				method.Name == "Run"
				&& method.IsGenericMethodDefinition
				&& method.GetParameters().Length == 1);

	public readonly record struct Argument(int Pointer, int Length);

	public sealed class ArgumentScope(WasmRuntime runtime) : IDisposable
	{
		private readonly WasmRuntime _runtime = runtime;
		private readonly List<Argument> _owned = new();

		public Argument Write(string value) => WriteBytes(Encoding.UTF8.GetBytes(value));

		public Argument WriteBytes(byte[] value)
		{
			var alloc = (Func<int, int>)AllocField.GetValue(_runtime)!;
			int pointer = alloc(value.Length);
			Memory memory = (Memory)MemoryField.GetValue(_runtime)!;
			value.AsSpan().CopyTo(memory.GetSpan((uint)pointer, value.Length));
			var argument = new Argument(pointer, value.Length);
			_owned.Add(argument);
			return argument;
		}

		public Argument WriteHandles(int[] handles)
		{
			byte[] bytes = new byte[handles.Length * 4];
			for (int index = 0; index < handles.Length; index++)
			{
				BitConverter.TryWriteBytes(bytes.AsSpan(index * 4, 4), handles[index]);
			}

			return WriteBytes(bytes);
		}

		public void Dispose()
		{
			var dealloc = (Action<int, int>)DeallocField.GetValue(_runtime)!;
			foreach (Argument argument in _owned)
			{
				dealloc(argument.Pointer, argument.Length);
			}
		}
	}
}
