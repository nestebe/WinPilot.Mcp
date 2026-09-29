using System.Diagnostics;
using WinPilot.Automation.Configuration;
using WinPilot.Automation.Errors;
using WinPilot.Automation.Windows;

namespace WinPilot.Automation.Sessions;

/// <summary>
/// Owns window-level session state: launched processes, the window registry, focus, close, and
/// shutdown policy. Callable from caller threads (Win32 fast paths); does not use UI Automation.
/// </summary>
internal sealed class SessionManager : IDisposable
{
    private readonly WinPilotOptions _options;
    private readonly LaunchService _launcher;
    private readonly Func<IReadOnlyList<RawWindow>> _windowEnumerator;
    private readonly Func<IntPtr, bool> _isWindowAlive;
    private readonly Func<IntPtr, bool> _tryClose;
    private readonly Func<IntPtr, bool> _tryFocus;
    private readonly Action<Process> _processKiller;
    private readonly TimeSpan _closePollInterval;
    private readonly Dictionary<int, Process> _ownedProcesses = [];
    private readonly object _gate = new();

    /// <summary>Initializes the session manager with optional test seams.</summary>
    public SessionManager(
        WinPilotOptions options,
        LaunchService launcher,
        Func<IReadOnlyList<RawWindow>>? windowEnumerator = null,
        Func<IntPtr, bool>? isWindowAlive = null,
        Func<IntPtr, bool>? tryClose = null,
        Func<IntPtr, bool>? tryFocus = null,
        Action<Process>? processKiller = null,
        TimeSpan? closePollInterval = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(launcher);

        _options = options;
        _launcher = launcher;
        _windowEnumerator = windowEnumerator ?? Win32WindowApi.EnumerateTopLevelWindows;
        _isWindowAlive = isWindowAlive ?? Win32WindowApi.IsWindowAlive;
        _tryClose = tryClose ?? Win32WindowApi.TryClose;
        _tryFocus = tryFocus ?? Win32WindowApi.TryFocus;
        _processKiller = processKiller ?? (static process => process.Kill(entireProcessTree: true));
        _closePollInterval = closePollInterval ?? TimeSpan.FromMilliseconds(100);
    }

    /// <summary>Gets the window registry for read access.</summary>
    public WindowRegistry Windows { get; } = new();

    /// <summary>Launches an application and returns its registered window.</summary>
    public async Task<WindowInfo> LaunchAsync(
        string app,
        IReadOnlyList<string>? args,
        int? timeoutMs,
        CancellationToken cancellationToken)
    {
        var result = await _launcher
            .LaunchAndWaitAsync(app, args, timeoutMs ?? _options.LaunchWindowTimeoutMs, TrackProcess, cancellationToken)
            .ConfigureAwait(false);

        return Windows.RegisterOrGet(
            result.Window.Hwnd,
            result.Window.Title,
            result.Window.ProcessId,
            Win32WindowApi.GetProcessName(result.Window.ProcessId));
    }

    /// <summary>Enumerates top-level windows, refreshes the registry, and prunes dead windows.</summary>
    public IReadOnlyList<WindowInfo> ListWindows()
    {
        Windows.Prune(hwnd => _isWindowAlive(hwnd));

        foreach (var window in _windowEnumerator())
        {
            Windows.RegisterOrGet(
                window.Hwnd,
                window.Title,
                window.ProcessId,
                Win32WindowApi.GetProcessName(window.ProcessId));
        }

        return Windows.Snapshot();
    }

    /// <summary>Focuses a window by handle or by case-insensitive title substring.</summary>
    public FocusResult Focus(string? handle, string? title)
    {
        if (!string.IsNullOrEmpty(handle))
        {
            var info = ResolveHandle(handle);
            return new FocusResult(info, _tryFocus(info.Hwnd));
        }

        if (!string.IsNullOrEmpty(title))
        {
            var match = ListWindows()
                .FirstOrDefault(window => window.Title.Contains(title, StringComparison.OrdinalIgnoreCase));

            if (match is null)
            {
                throw new WindowNotFoundException(
                    $"No window title contains '{title}'.",
                    "Run windows_list_windows to see available windows.");
            }

            return new FocusResult(match, _tryFocus(match.Hwnd));
        }

        throw new InvalidArgumentException(
            "Either 'handle' or 'title' is required.",
            "Pass a window handle from windows_list_windows or a title substring.");
    }

    /// <summary>Closes a window gracefully; <paramref name="force"/> kills the process of owned apps.</summary>
    public async Task CloseAsync(string handle, bool force, CancellationToken cancellationToken)
    {
        var info = ResolveHandle(handle);
        _ = _tryClose(info.Hwnd);

        var deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(_options.CloseWindowTimeoutMs);
        while (_isWindowAlive(info.Hwnd) && DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(_closePollInterval, cancellationToken).ConfigureAwait(false);
        }

        if (_isWindowAlive(info.Hwnd))
        {
            if (force && TryGetOwnedProcess(info.ProcessId, out var process))
            {
                _processKiller(process);
            }
            else
            {
                throw new OperationTimeoutException(
                    $"Window {handle} did not close within {_options.CloseWindowTimeoutMs} ms.",
                    "The app may show a save prompt. Dismiss it, or retry windows_close with force: true for apps launched by this server.");
            }
        }

        Windows.Prune(hwnd => _isWindowAlive(hwnd));
    }

    /// <summary>Returns whether the process was launched by this session.</summary>
    public bool IsOwned(int processId)
    {
        lock (_gate)
        {
            return _ownedProcesses.ContainsKey(processId);
        }
    }

    /// <summary>Closes apps launched by this session unless <see cref="WinPilotOptions.KeepAppsOnExit"/> is set.</summary>
    public void Shutdown()
    {
        List<Process> toKill;
        lock (_gate)
        {
            if (_options.KeepAppsOnExit)
            {
                return;
            }

            toKill = [.. _ownedProcesses.Values.Distinct()];
            _ownedProcesses.Clear();
        }

        foreach (var process in toKill)
        {
            try
            {
                if (!process.HasExited)
                {
                    _processKiller(process);
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // Best effort: the process may have exited between the check and the kill.
            }
        }
    }

    /// <inheritdoc />
    public void Dispose() => Shutdown();

    private WindowInfo ResolveHandle(string handle)
    {
        var info = Windows.Get(handle);
        if (info is null || !_isWindowAlive(info.Hwnd))
        {
            Windows.Prune(hwnd => _isWindowAlive(hwnd));
            throw new WindowNotFoundException(
                $"Window not found: {handle}.",
                "Run windows_list_windows to see available windows.");
        }

        return info;
    }

    private bool TryGetOwnedProcess(int processId, out Process process)
    {
        lock (_gate)
        {
            return _ownedProcesses.TryGetValue(processId, out process!);
        }
    }

    private void TrackProcess(Process process)
    {
        lock (_gate)
        {
            _ownedProcesses[process.Id] = process;
        }
    }
}

/// <summary>Outcome of a focus request: the window and whether the native focus call succeeded.</summary>
internal sealed record FocusResult(WindowInfo Window, bool NativeFocusSucceeded);
