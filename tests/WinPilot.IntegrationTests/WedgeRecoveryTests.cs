using System.Diagnostics;
using WinPilot.Automation.Errors;
using Xunit;

namespace WinPilot.IntegrationTests;

public class WedgeRecoveryTests
{
    [Fact]
    public async Task Wedged_ui_soft_times_out_keeps_window_tools_alive_and_recovers()
    {
        TestEnvironment.RequireInteractiveDesktop();

        await using var fixture = await AutomationFixture.StartAsync(
            options =>
            {
                options.OperationTimeoutSeconds = 1;
                options.HardTimeoutSeconds = 2;
            },
            hangMs: 6_000);

        var first = await fixture.Engine.SnapshotAsync(fixture.Window.Handle, null, CancellationToken.None);
        var helloRef = TestHelpers.FindRef(first, "Hello");
        var hangRef = TestHelpers.FindRef(first, "Hang UI");

        // Start the hang; the click itself may time out because the handler blocks the UI thread.
        var wedgedClick = fixture.Engine.ClickAsync(hangRef, "left", false, CancellationToken.None);
        await Task.Delay(700, CancellationToken.None);

        // A snapshot now hits the wedged provider and fails with the soft timeout, not a hang.
        var stopwatch = Stopwatch.StartNew();
        await Assert.ThrowsAsync<OperationTimeoutException>(() =>
            fixture.Engine.SnapshotAsync(fixture.Window.Handle, null, CancellationToken.None));
        stopwatch.Stop();
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(3), $"expected the soft timeout, took {stopwatch.Elapsed}");

        // The Win32 fast path keeps working while the UI Automation worker is wedged.
        var windows = await fixture.Engine.ListWindowsAsync(CancellationToken.None);
        Assert.Contains(windows, window => window.Hwnd == fixture.Window.Hwnd);

        // Once the app responds again, snapshots and refs from before the wedge work again.
        await Task.Delay(5_500, CancellationToken.None);
        var recovered = await fixture.Engine.SnapshotAsync(fixture.Window.Handle, null, CancellationToken.None);
        Assert.Contains("button \"Hello\"", recovered, StringComparison.Ordinal);
        Assert.Contains("Invoked Hello", await fixture.Engine.ClickAsync(helloRef, "left", false, CancellationToken.None), StringComparison.Ordinal);

        await TestHelpers.ObserveAsync(wedgedClick);
    }
}
