using System.Diagnostics;
using WinPilot.Automation.Errors;
using WinPilot.Automation.Windows;

namespace WinPilot.Automation.Sessions;

/// <summary>Outcome of a successful launch.</summary>
internal sealed record LaunchResult(Process? Process, RawWindow Window);

/// <summary>
/// Launches applications and waits for their window to appear: process id polling for direct
/// launches, visible-window diffing for UWP/shell activations. Test seams are injectable.
/// </summary>
internal sealed class LaunchService
{
    private readonly Func<ProcessStartInfo, Process?> _processStarter;
    private readonly Func<IReadOnlyList<RawWindow>> _windowEnumerator;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    /// <summary>Initializes the service with optional test seams.</summary>
    public LaunchService(
        Func<ProcessStartInfo, Process?>? processStarter = null,
        Func<IReadOnlyList<RawWindow>>? windowEnumerator = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _processStarter = processStarter ?? (static psi => Process.Start(psi));
        _windowEnumerator = windowEnumerator ?? Win32WindowApi.EnumerateTopLevelWindows;
        _delay = delay ?? (static (timeSpan, ct) => Task.Delay(timeSpan, ct));
    }

    /// <summary>Starts the application and waits for its first top-level window.</summary>
    public async Task<LaunchResult> LaunchAndWaitAsync(
        string app,
        IReadOnlyList<string>? args,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(app);
        ArgumentOutOfRangeException.ThrowIfLessThan(timeoutMs, 1);

        return app.Contains('!')
            ? await LaunchUwpAsync(app, timeoutMs, cancellationToken).ConfigureAwait(false)
            : await LaunchProcessAsync(app, args, timeoutMs, cancellationToken).ConfigureAwait(false);
    }

    private async Task<LaunchResult> LaunchProcessAsync(
        string app,
        IReadOnlyList<string>? args,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(app)
        {
            UseShellExecute = false,
            Arguments = string.Join(' ', args ?? []),
        };

        var process = _processStarter(startInfo)
            ?? throw new LaunchFailedException(
                $"Failed to start '{app}'.",
                "Check that the executable path exists and that you have permission to run it.");

        var window = await PollAsync(
            () => FindFirst(w => w.ProcessId == process.Id),
            timeoutMs,
            cancellationToken).ConfigureAwait(false);

        if (window is null)
        {
            throw new LaunchFailedException(
                $"No window appeared for '{app}' (pid {process.Id}) within {timeoutMs} ms.",
                "The app may still be starting: retry windows_list_windows, or use windows_focus with a title substring.");
        }

        return new LaunchResult(process, window.Value);
    }

    private async Task<LaunchResult> LaunchUwpAsync(string appId, int timeoutMs, CancellationToken cancellationToken)
    {
        var baseline = _windowEnumerator().Select(w => w.Hwnd).ToHashSet();

        var startInfo = new ProcessStartInfo("explorer.exe")
        {
            UseShellExecute = false,
            Arguments = $"shell:AppsFolder\\{appId}",
        };

        _ = _processStarter(startInfo);

        var window = await PollAsync(
            () => FindFirst(w => !baseline.Contains(w.Hwnd)),
            timeoutMs,
            cancellationToken).ConfigureAwait(false);

        if (window is null)
        {
            throw new LaunchFailedException(
                $"No new window appeared for '{appId}' within {timeoutMs} ms.",
                "Check the UWP app id (for example 'Microsoft.WindowsCalculator_8wekyb3d8bbwe!App'); the app may also have opened an existing window.");
        }

        return new LaunchResult(null, window.Value);
    }

    private async Task<RawWindow?> PollAsync(Func<RawWindow?> probe, int timeoutMs, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (probe() is { } found)
            {
                return found;
            }

            await _delay(TimeSpan.FromMilliseconds(100), cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    private RawWindow? FindFirst(Func<RawWindow, bool> predicate)
    {
        foreach (var window in _windowEnumerator())
        {
            if (predicate(window))
            {
                return window;
            }
        }

        return null;
    }
}
