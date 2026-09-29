using WinPilot.Automation.Errors;
using Xunit;

namespace WinPilot.IntegrationTests;

public class SnapshotIntegrationTests
{
    [Fact]
    public async Task Snapshot_contains_the_expected_controls_with_refs()
    {
        TestEnvironment.RequireInteractiveDesktop();
        await using var fixture = await AutomationFixture.StartAsync();

        var text = await fixture.Engine.SnapshotAsync(fixture.Window.Handle, null, CancellationToken.None);

        Assert.Contains($"window \"{fixture.Window.Title}\"", text, StringComparison.Ordinal);
        Assert.Contains("button \"Hello\"", text, StringComparison.Ordinal);
        Assert.Contains("button \"Hang UI\"", text, StringComparison.Ordinal);
        Assert.Contains("\"[txtName]\"", text, StringComparison.Ordinal);
        Assert.Matches("""\[ref=w\d+e\d+\]""", text);
    }

    [Fact]
    public async Task Snapshot_depth_is_bounded_by_the_requested_max_depth()
    {
        TestEnvironment.RequireInteractiveDesktop();
        await using var fixture = await AutomationFixture.StartAsync();

        var text = await fixture.Engine.SnapshotAsync(fixture.Window.Handle, maxDepth: 1, CancellationToken.None);

        Assert.Contains("(truncated: depth limit)", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unknown_handles_report_window_not_found()
    {
        TestEnvironment.RequireInteractiveDesktop();
        await using var fixture = await AutomationFixture.StartAsync();

        var exception = await Assert.ThrowsAsync<WindowNotFoundException>(() =>
            fixture.Engine.SnapshotAsync("w99", null, CancellationToken.None));

        Assert.NotNull(exception.Hint);
    }

    [Fact]
    public async Task Refs_for_never_snapshotted_windows_report_element_not_found()
    {
        await using var fixture = await AutomationFixture.StartAsync();

        var exception = await Assert.ThrowsAsync<ElementNotFoundException>(() =>
            fixture.Engine.GetTextAsync("w99e99", CancellationToken.None));

        Assert.NotNull(exception.Hint);
    }

    [Fact]
    public async Task Unknown_refs_report_element_not_found()
    {
        TestEnvironment.RequireInteractiveDesktop();
        await using var fixture = await AutomationFixture.StartAsync();
        _ = await fixture.Engine.SnapshotAsync(fixture.Window.Handle, null, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<ElementNotFoundException>(() =>
            fixture.Engine.ClickAsync($"{fixture.Window.Handle}e9999", "left", false, CancellationToken.None));

        Assert.Contains("windows_snapshot", exception.Hint!, StringComparison.Ordinal);
    }
}
