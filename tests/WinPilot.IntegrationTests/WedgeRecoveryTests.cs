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

        var first = await SnapshotWithRetryAsync(fixture);
        var hangRef = TestHelpers.FindRef(first, "Hang UI");

        // Start the hang; the click itself may time out because the handler blocks the UI thread.
        var wedgedClick = fixture.Engine.ClickAsync(hangRef, "left", false, CancellationToken.None);

        // A snapshot now hits the wedged provider and fails with the soft timeout, not a hang.
        // On loaded runners the click may take a moment to reach the UI thread, so observe until
        // the soft timeout actually happens.
        var observedTimeout = false;
        for (var attempt = 0; attempt < 6 && !observedTimeout; attempt++)
        {
            try
            {
                _ = await fixture.Engine.SnapshotAsync(fixture.Window.Handle, null, CancellationToken.None);
                await Task.Delay(300, TestContext.Current.CancellationToken); // not hung yet
            }
            catch (OperationTimeoutException)
            {
                observedTimeout = true;
            }
        }

        Assert.True(observedTimeout, "expected the soft timeout while the app was hung");

        // The Win32 fast path keeps working while the UI Automation worker is wedged.
        var windows = await fixture.Engine.ListWindowsAsync(CancellationToken.None);
        Assert.Contains(windows, window => window.Hwnd == fixture.Window.Hwnd);

        // Once the app responds again, snapshots succeed on a healthy worker again.
        // (The click path is covered by InteractionIntegrationTests with production timeouts;
        // clicking here under a 1 s soft timeout is too environment-sensitive to assert.)
        await Task.Delay(5_500, CancellationToken.None);
        var recovered = await SnapshotWithRetryAsync(fixture);
        Assert.Contains("button \"Hello\"", recovered, StringComparison.Ordinal);

        await TestHelpers.ObserveAsync(wedgedClick);
    }

    /// <summary>
    /// The engine is configured with a 1 s soft timeout for the wedge assertions; on loaded CI
    /// runners even a healthy snapshot can exceed that, so setup and recovery retry.
    /// </summary>
    private static async Task<string> SnapshotWithRetryAsync(AutomationFixture fixture)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await fixture.Engine.SnapshotAsync(fixture.Window.Handle, null, CancellationToken.None);
            }
            catch (Exception exception) when (
                exception is OperationTimeoutException or EngineBusyException && attempt < 3)
            {
                await Task.Delay(750, TestContext.Current.CancellationToken);
            }
        }
    }
}
