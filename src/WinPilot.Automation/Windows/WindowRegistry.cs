namespace WinPilot.Automation.Windows;

/// <summary>
/// Maps native windows to stable agent-facing handles (<c>w1</c>, <c>w2</c>, ...).
/// Thread-safe: usable from caller threads (Win32 fast paths) and the UI Automation worker alike.
/// </summary>
internal sealed class WindowRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<string, WindowInfo> _byHandle = new(StringComparer.Ordinal);
    private readonly Dictionary<IntPtr, string> _byHwnd = [];
    private int _counter;

    /// <summary>Registers a window or refreshes the metadata of an already-known one.</summary>
    public WindowInfo RegisterOrGet(IntPtr hwnd, string title, int processId, string? processName)
    {
        lock (_gate)
        {
            if (_byHwnd.TryGetValue(hwnd, out var existingHandle))
            {
                var refreshed = new WindowInfo(existingHandle, hwnd, title, processId, processName);
                _byHandle[existingHandle] = refreshed;
                return refreshed;
            }

            var handle = $"w{++_counter}";
            var info = new WindowInfo(handle, hwnd, title, processId, processName);
            _byHandle[handle] = info;
            _byHwnd[hwnd] = handle;
            return info;
        }
    }

    /// <summary>Gets the window info for a handle, or null when unknown.</summary>
    public WindowInfo? Get(string handle)
    {
        lock (_gate)
        {
            return _byHandle.GetValueOrDefault(handle);
        }
    }

    /// <summary>Removes every window the <paramref name="isAlive"/> predicate reports as dead.</summary>
    public void Prune(Func<IntPtr, bool> isAlive)
    {
        ArgumentNullException.ThrowIfNull(isAlive);

        lock (_gate)
        {
            foreach (var (hwnd, handle) in _byHwnd.Where(pair => !isAlive(pair.Key)).ToList())
            {
                _byHwnd.Remove(hwnd);
                _byHandle.Remove(handle);
            }
        }
    }

    /// <summary>Returns a snapshot of all known windows.</summary>
    public IReadOnlyList<WindowInfo> Snapshot()
    {
        lock (_gate)
        {
            return [.. _byHandle.Values];
        }
    }
}
