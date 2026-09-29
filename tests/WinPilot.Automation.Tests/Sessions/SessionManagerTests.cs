using System.Diagnostics;
using WinPilot.Automation.Configuration;
using WinPilot.Automation.Errors;
using WinPilot.Automation.Sessions;
using WinPilot.Automation.Windows;

namespace WinPilot.Automation.Tests.Sessions;

public class SessionManagerTests
{
    private static Process CurrentProcess => Process.GetCurrentProcess();

    private sealed class Harness
    {
        public List<RawWindow> Windows { get; } = [];

        public HashSet<IntPtr> Alive { get; } = [];

        public List<IntPtr> Closed { get; } = [];

        public List<Process> Killed { get; } = [];

        public List<IntPtr> Focused { get; } = [];

        public bool TryFocusResult { get; set; } = true;

        public bool StubbornClose { get; set; }

        public LaunchService Launcher { get; }

        public Harness()
        {
            Launcher = new LaunchService(
                processStarter: _ => CurrentProcess,
                windowEnumerator: () => Windows,
                delay: (_, _) => Task.CompletedTask);
        }

        public SessionManager CreateManager(bool keepAppsOnExit = false, Func<int, Process?>? processLookup = null)
        {
            var options = new WinPilotOptions { KeepAppsOnExit = keepAppsOnExit, CloseWindowTimeoutMs = 50 };
            return new SessionManager(
                options,
                Launcher,
                windowEnumerator: () => Windows,
                isWindowAlive: Alive.Contains,
                tryClose: hwnd =>
                {
                    Closed.Add(hwnd);
                    if (!StubbornClose)
                    {
                        Alive.Remove(hwnd);
                    }

                    return true;
                },
                tryFocus: hwnd =>
                {
                    Focused.Add(hwnd);
                    return TryFocusResult;
                },
                processKiller: Killed.Add,
                closePollInterval: TimeSpan.FromMilliseconds(5),
                processLookup: processLookup);
        }
    }

    [Fact]
    public void List_windows_registers_and_prunes_dead_windows()
    {
        var harness = new Harness();
        using var manager = harness.CreateManager();
        harness.Windows.Add(new RawWindow(new IntPtr(1), "A", 10));
        harness.Windows.Add(new RawWindow(new IntPtr(2), "B", 11));
        harness.Alive.UnionWith([new IntPtr(1), new IntPtr(2)]);

        var first = manager.ListWindows();
        Assert.Equal(2, first.Count);
        Assert.Equal("w1", first[0].Handle);

        harness.Windows.RemoveAll(w => w.Hwnd == new IntPtr(1));
        harness.Alive.Remove(new IntPtr(1));

        var second = manager.ListWindows();
        Assert.Single(second);
        Assert.Equal("w2", second[0].Handle);
    }

    [Fact]
    public void Focus_by_title_matches_case_insensitive_substring()
    {
        var harness = new Harness();
        using var manager = harness.CreateManager();
        harness.Windows.Add(new RawWindow(new IntPtr(1), "other", 10));
        harness.Windows.Add(new RawWindow(new IntPtr(2), "Untitled - Notepad", 11));
        harness.Alive.UnionWith([new IntPtr(1), new IntPtr(2)]);

        var result = manager.Focus(handle: null, title: "notepad");

        Assert.Equal("w2", result.Window.Handle);
        Assert.True(result.NativeFocusSucceeded);
        Assert.Equal([new IntPtr(2)], harness.Focused);
    }

    [Fact]
    public void Focus_reports_when_native_focus_fails()
    {
        var harness = new Harness { TryFocusResult = false };
        using var manager = harness.CreateManager();
        harness.Windows.Add(new RawWindow(new IntPtr(1), "App", 10));
        harness.Alive.Add(new IntPtr(1));
        _ = manager.ListWindows();

        var result = manager.Focus("w1", null);

        Assert.Equal("w1", result.Window.Handle);
        Assert.False(result.NativeFocusSucceeded);
    }

    [Fact]
    public void Focus_with_unknown_handle_throws_window_not_found()
    {
        var harness = new Harness();
        using var manager = harness.CreateManager();

        var exception = Assert.Throws<WindowNotFoundException>(() => manager.Focus("w9", null));
        Assert.Contains("windows_list_windows", exception.Hint!, StringComparison.Ordinal);
    }

    [Fact]
    public void Focus_without_target_throws_invalid_argument()
    {
        var harness = new Harness();
        using var manager = harness.CreateManager();

        Assert.Throws<InvalidArgumentException>(() => manager.Focus(null, null));
    }

    [Fact]
    public async Task Close_posts_wm_close_and_removes_the_window_on_success()
    {
        var harness = new Harness();
        using var manager = harness.CreateManager();
        harness.Windows.Add(new RawWindow(new IntPtr(1), "A", 10));
        harness.Alive.Add(new IntPtr(1));
        var info = manager.ListWindows()[0];

        await manager.CloseAsync(info.Handle, force: false, CancellationToken.None);

        Assert.Equal([new IntPtr(1)], harness.Closed);
        Assert.Null(manager.Windows.Get(info.Handle));
    }

    [Fact]
    public async Task Close_that_times_out_without_force_reports_a_timeout()
    {
        var harness = new Harness { StubbornClose = true };
        using var manager = harness.CreateManager();
        harness.Windows.Add(new RawWindow(new IntPtr(1), "Stubborn", 10));
        harness.Alive.Add(new IntPtr(1));
        var info = manager.ListWindows()[0];

        await Assert.ThrowsAsync<OperationTimeoutException>(() =>
            manager.CloseAsync(info.Handle, force: false, CancellationToken.None));

        Assert.Empty(harness.Killed);
    }

    [Fact]
    public async Task Force_close_kills_only_owned_processes()
    {
        var harness = new Harness { StubbornClose = true };
        using var manager = harness.CreateManager();
        harness.Windows.Add(new RawWindow(new IntPtr(1), "Stubborn", CurrentProcess.Id));
        harness.Alive.Add(new IntPtr(1));

        var info = await manager.LaunchAsync("testapp.exe", null, timeoutMs: 5_000, CancellationToken.None);

        await manager.CloseAsync(info.Handle, force: true, CancellationToken.None);

        var killed = Assert.Single(harness.Killed);
        Assert.Equal(CurrentProcess.Id, killed.Id);
    }

    [Fact]
    public async Task Shutdown_closes_owned_apps_unless_keep_apps_is_set()
    {
        var harness = new Harness();
        harness.Windows.Add(new RawWindow(new IntPtr(1), "App", CurrentProcess.Id));
        harness.Alive.Add(new IntPtr(1));

        var closing = harness.CreateManager(keepAppsOnExit: false);
        await closing.LaunchAsync("testapp.exe", null, timeoutMs: 5_000, CancellationToken.None);
        closing.Shutdown();
        closing.Dispose();
        var killed = Assert.Single(harness.Killed);
        Assert.Equal(CurrentProcess.Id, killed.Id);

        harness.Killed.Clear();
        var keeping = harness.CreateManager(keepAppsOnExit: true);
        await keeping.LaunchAsync("testapp.exe", null, timeoutMs: 5_000, CancellationToken.None);
        keeping.Shutdown();
        keeping.Dispose();
        Assert.Empty(harness.Killed);
    }

    [Fact]
    public async Task Launch_through_a_launcher_stub_also_tracks_the_window_process()
    {
        var calls = 0;
        var packagedWindow = new RawWindow(new IntPtr(1), "Packaged", 99); // window owned by another process
        var launcher = new LaunchService(
            processStarter: _ => CurrentProcess,
            windowEnumerator: () => Interlocked.Increment(ref calls) == 1 ? [] : [packagedWindow],
            delay: (_, _) => Task.CompletedTask);
        var options = new WinPilotOptions { CloseWindowTimeoutMs = 50 };
        using var manager = new SessionManager(
            options,
            launcher,
            windowEnumerator: () => [packagedWindow],
            isWindowAlive: _ => true,
            processKiller: _ => { },
            processLookup: pid => pid == 99 ? CurrentProcess : null);

        var info = await manager.LaunchAsync("alias.exe", null, timeoutMs: 1, CancellationToken.None);

        Assert.Equal(99, info.ProcessId);
        Assert.True(manager.IsOwned(99), "the window's process must be tracked when a launcher stub produced the window");
    }

    [Fact]
    public async Task Launch_failure_still_tracks_the_started_process()
    {
        var harness = new Harness(); // no windows appear
        using var manager = harness.CreateManager();

        await Assert.ThrowsAsync<LaunchFailedException>(() =>
            manager.LaunchAsync("testapp.exe", null, timeoutMs: 1, CancellationToken.None));

        Assert.True(manager.IsOwned(CurrentProcess.Id), "the started process must be tracked even when discovery fails");

        manager.Shutdown();
        Assert.Contains(harness.Killed, process => process.Id == CurrentProcess.Id);
    }

    [Fact]
    public void Shutdown_never_touches_non_owned_processes()
    {
        var harness = new Harness();
        using var manager = harness.CreateManager();
        harness.Windows.Add(new RawWindow(new IntPtr(1), "Foreign", 999_999));
        harness.Alive.Add(new IntPtr(1));
        _ = manager.ListWindows();

        manager.Shutdown();

        Assert.Empty(harness.Killed);
    }
}
