using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WinPilot.Automation.Capture;
using WinPilot.Automation.Configuration;
using WinPilot.Automation.Errors;

namespace WinPilot.Automation.Tests;

public class WindowsAutomationEngineTests
{
    private static WindowsAutomationEngine CreateEngine()
        => new(Options.Create(new WinPilotOptions()), NullLoggerFactory.Instance);

    [Fact]
    public async Task Wait_for_requires_a_ref_or_a_selector()
    {
        await using var engine = CreateEngine();

        var exception = await Assert.ThrowsAsync<InvalidArgumentException>(() =>
            engine.WaitForElementAsync(null, null, null, null, CancellationToken.None));

        Assert.Contains("selector", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Background_capture_requires_a_handle_and_no_ref_or_fullscreen()
    {
        await using var engine = CreateEngine();

        await Assert.ThrowsAsync<InvalidArgumentException>(() =>
            engine.CaptureAsync(new CaptureRequest(null, "w1e1", false, true, null, false), CancellationToken.None));

        await Assert.ThrowsAsync<InvalidArgumentException>(() =>
            engine.CaptureAsync(new CaptureRequest(null, null, true, true, null, false), CancellationToken.None));
    }

    [Theory]
    [InlineData(50, 10, 20)]
    [InlineData(0, 10, 1)]
    [InlineData(-5, 10, 1)]
    [InlineData(5, 10, 5)]
    [InlineData(null, 10, 10)]
    [InlineData(null, 25, 20)]
    public void Snapshot_depth_is_clamped_between_1_and_20(int? requested, int configured, int expected)
        => Assert.Equal(expected, WindowsAutomationEngine.ClampDepth(requested, configured));

    [Fact]
    public async Task Dispose_is_idempotent()
    {
        var engine = CreateEngine();

        await engine.DisposeAsync();
        await engine.DisposeAsync();
    }
}
