using System.Diagnostics;
using WinPilot.Automation.Errors;
using WinPilot.Automation.Sessions;
using WinPilot.Automation.Windows;

namespace WinPilot.Automation.Tests.Sessions;

public class LaunchServiceTests
{
    private static Process CurrentProcess => Process.GetCurrentProcess();

    [Fact]
    public async Task Direct_launch_polls_until_the_process_window_appears()
    {
        var polls = 0;
        var current = CurrentProcess;
        var launcher = new LaunchService(
            processStarter: _ => current,
            windowEnumerator: () =>
            {
                polls++;
                return polls < 3
                    ? []
                    : [new RawWindow(new IntPtr(0x10), "Test App", current.Id)];
            },
            delay: (_, _) => Task.CompletedTask);

        var result = await launcher.LaunchAndWaitAsync("testapp.exe", ["--flag"], timeoutMs: 5_000, cancellationToken: CancellationToken.None);

        Assert.Equal(new IntPtr(0x10), result.Window.Hwnd);
        Assert.Equal("Test App", result.Window.Title);
        Assert.Same(current, result.Process);
        Assert.Equal(3, polls);
    }

    [Fact]
    public async Task Direct_launch_forwards_executable_and_arguments()
    {
        var current = CurrentProcess;
        ProcessStartInfo? captured = null;
        var launcher = new LaunchService(
            processStarter: psi =>
            {
                captured = psi;
                return current;
            },
            windowEnumerator: () => [new RawWindow(new IntPtr(1), "App", current.Id)],
            delay: (_, _) => Task.CompletedTask);

        await launcher.LaunchAndWaitAsync("app.exe", ["--flag", "with space"], timeoutMs: 5_000, cancellationToken: CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal("app.exe", captured.FileName);
        Assert.Equal(["--flag", "with space"], captured.ArgumentList);
        Assert.False(captured.UseShellExecute);
    }

    [Fact]
    public async Task Non_positive_timeouts_are_rejected_as_invalid_arguments()
    {
        var launcher = new LaunchService(
            processStarter: _ => CurrentProcess,
            windowEnumerator: () => [],
            delay: (_, _) => Task.CompletedTask);

        await Assert.ThrowsAsync<InvalidArgumentException>(() =>
            launcher.LaunchAndWaitAsync("app.exe", null, timeoutMs: 0, cancellationToken: CancellationToken.None));
    }

    [Fact]
    public async Task Direct_launch_times_out_with_an_actionable_error()
    {
        var launcher = new LaunchService(
            processStarter: _ => CurrentProcess,
            windowEnumerator: () => [],
            delay: (_, _) => Task.CompletedTask);

        var exception = await Assert.ThrowsAsync<LaunchFailedException>(() =>
            launcher.LaunchAndWaitAsync("ghost.exe", null, timeoutMs: 1, cancellationToken: CancellationToken.None));

        Assert.Contains("ghost.exe", exception.Message, StringComparison.Ordinal);
        Assert.NotNull(exception.Hint);
    }

    [Fact]
    public async Task Uwp_launch_uses_explorer_shell_and_detects_a_new_window()
    {
        ProcessStartInfo? captured = null;
        var calls = 0;
        var launcher = new LaunchService(
            processStarter: psi =>
            {
                captured = psi;
                return null;
            },
            windowEnumerator: () =>
            {
                calls++;
                return calls < 2
                    ? [new RawWindow(new IntPtr(1), "Existing", 10)]
                    : [new RawWindow(new IntPtr(1), "Existing", 10), new RawWindow(new IntPtr(2), "Calculator", 20)];
            },
            delay: (_, _) => Task.CompletedTask);

        var result = await launcher.LaunchAndWaitAsync(
            "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", null, timeoutMs: 5_000, cancellationToken: CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal("explorer.exe", captured.FileName);
        Assert.Contains("shell:AppsFolder\\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", captured.Arguments, StringComparison.Ordinal);
        Assert.Equal(new IntPtr(2), result.Window.Hwnd);
        Assert.Null(result.Process);
    }

    [Fact]
    public async Task Starter_returning_null_fails_with_a_launch_error()
    {
        var launcher = new LaunchService(
            processStarter: _ => null,
            windowEnumerator: () => [],
            delay: (_, _) => Task.CompletedTask);

        await Assert.ThrowsAsync<LaunchFailedException>(() =>
            launcher.LaunchAndWaitAsync("app.exe", null, timeoutMs: 5_000, cancellationToken: CancellationToken.None));
    }

    [Fact]
    public async Task Cancellation_is_honored_while_polling()
    {
        using var cts = new CancellationTokenSource();
        var launcher = new LaunchService(
            processStarter: _ => CurrentProcess,
            windowEnumerator: () => [],
            delay: (_, ct) =>
            {
                cts.Cancel();
                ct.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            launcher.LaunchAndWaitAsync("app.exe", null, timeoutMs: 60_000, cancellationToken: cts.Token));
    }
}
