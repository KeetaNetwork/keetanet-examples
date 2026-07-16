using KeetaNet.Anchor;

namespace KeetaNet.Examples.Ledger;

/// <summary>Owns one guest handle and frees it on dispose.</summary>
public abstract class GuestHandle : IDisposable
{
	private int _handle;

	protected GuestHandle(WasmRuntime runtime, int handle)
	{
		Runtime = runtime;
		_handle = handle;
	}

	protected WasmRuntime Runtime { get; }

	protected int Handle => _handle != 0 ? _handle : throw new ObjectDisposedException(GetType().Name);

	internal int RawHandle => Handle;

	protected abstract void Release(int handle);

	protected int ConsumeHandle()
	{
		if (_handle == 0)
		{
			throw new InvalidOperationException($"{GetType().Name} has been consumed");
		}

		int current = _handle;
		_handle = 0;
		return current;
	}

	public void Dispose()
	{
		if (_handle == 0)
		{
			return;
		}

		int handle = _handle;
		_handle = 0;
		Release(handle);
	}
}
