using System.Diagnostics;
using Xunit;

namespace WinPilot.IntegrationTests;

public class SessionIntegrationTests
{
    [Fact]
    public async Task List_windows_contains_the_test_app()
    {
        TestEnvironment.RequireInteractiveDesktop();
        await using var fixture = await AutomationFixture.StartAsync();

        var windows = await fixture.Engine.ListWindowsAsync(CancellationToken.None);

        Assert.Contains(windows, window => window.Hwnd == fixture.Window.Hwnd && window.Title == fixture.Window.Title);
    }

    [Fact]
    public async Task Focus_works_by_title_substring_and_by_handle()
    {
        TestEnvironment.RequireInteractiveDesktop();
        await using var fixture = await AutomationFixture.StartAsync();

        var byTitle = await fixture.Engine.FocusWindowAsync(null, "winpilot winforms", CancellationToken.None);
        var byHandle = await fixture.Engine.FocusWindowAsync(fixture.Window.Handle, null, CancellationToken.None);

        Assert.Equal(fixture.Window.Hwnd, byTitle.Hwnd);
        Assert.Equal(fixture.Window.Handle, byHandle.Handle);
    }

    [Fact]
    public async Task Close_gracefully_removes_the_window()
    {
        TestEnvironment.RequireInteractiveDesktop();
        await using var fixture = await AutomationFixture.StartAsync();
        var second = await fixture.Engine.LaunchAsync(AutomationFixture.TestAppPath, null, timeoutMs: 20_000, CancellationToken.None);

        await fixture.Engine.CloseWindowAsync(second.Handle, force: false, CancellationToken.None);

        var windows = await fixture.Engine.ListWindowsAsync(CancellationToken.None);
        Assert.DoesNotContain(windows, window => window.Handle == second.Handle);
    }

    [Fact]
    public async Task Dispose_closes_apps_launched_by_the_engine()
    {
        TestEnvironment.RequireInteractiveDesktop();
        var fixture = await AutomationFixture.StartAsync();
        var processId = fixture.Window.ProcessId;

        await fixture.DisposeAsync();

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline && !HasExited(processId))
        {
            await Task.Delay(100, CancellationToken.None);
        }

        Assert.True(HasExited(processId), $"test app process {processId} was not closed on dispose");
    }

    private static bool HasExited(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.HasExited;
        }
        catch (ArgumentException)
        {
            return true; // no longer running
        }
    }
}
