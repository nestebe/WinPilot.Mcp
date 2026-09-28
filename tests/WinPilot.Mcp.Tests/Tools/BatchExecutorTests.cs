using WinPilot.Automation.Errors;
using WinPilot.Mcp.Tests.Fakes;
using WinPilot.Mcp.Tools;

namespace WinPilot.Mcp.Tests.Tools;

public class BatchExecutorTests
{
    [Fact]
    public async Task Runs_actions_sequentially_in_order()
    {
        var fake = new FakeWindowsAutomation();
        var executor = new BatchExecutor(fake);

        var output = await executor.RunAsync(
            [
                new BatchAction("click", Ref: "w1e1"),
                new BatchAction("type", Ref: "w1e2", Text: "hi"),
                new BatchAction("fill", Ref: "w1e3", Value: "value"),
            ],
            stopOnError: true,
            CancellationToken.None);

        Assert.Equal(
            "1. click: Invoked w1e1\n" +
            "2. type: Typed \"hi\"\n" +
            "3. fill: Filled w1e3 with \"value\"",
            output);
        Assert.Equal(["click:w1e1|left|False", "type:w1e2|hi|False", "fill:w1e3|value"], fake.Calls);
    }

    [Fact]
    public async Task Stops_at_the_first_error_by_default()
    {
        var fake = new FakeWindowsAutomation
        {
            OnClick = (_, _, _, _) => throw new ElementNotFoundException("Element not found: w9e9.", "Run windows_snapshot."),
        };
        var executor = new BatchExecutor(fake);

        var output = await executor.RunAsync(
            [
                new BatchAction("click", Ref: "w9e9"),
                new BatchAction("click", Ref: "w1e1"),
            ],
            stopOnError: true,
            CancellationToken.None);

        Assert.Equal(
            "1. ERROR: ELEMENT_NOT_FOUND: Element not found: w9e9. (Run windows_snapshot.)\n" +
            "Stopped at action 1 due to error",
            output);
        Assert.Single(fake.Calls);
    }

    [Fact]
    public async Task Continues_after_errors_when_stop_on_error_is_false()
    {
        var fake = new FakeWindowsAutomation
        {
            OnClick = (elementRef, _, _, _) => elementRef == "w9e9"
                ? throw new ElementNotFoundException("Element not found: w9e9.")
                : Task.FromResult("Invoked w1e1"),
        };
        var executor = new BatchExecutor(fake);

        var output = await executor.RunAsync(
            [
                new BatchAction("click", Ref: "w9e9"),
                new BatchAction("click", Ref: "w1e1"),
            ],
            stopOnError: false,
            CancellationToken.None);

        Assert.Equal(
            "1. ERROR: ELEMENT_NOT_FOUND: Element not found: w9e9.\n" +
            "2. click: Invoked w1e1",
            output);
    }

    [Fact]
    public async Task Wait_action_uses_the_requested_duration()
    {
        var executor = new BatchExecutor(new FakeWindowsAutomation());

        var output = await executor.RunAsync([new BatchAction("wait", Ms: 50)], stopOnError: true, CancellationToken.None);

        Assert.Equal("1. wait: Waited 50ms", output);
    }

    [Fact]
    public async Task Snapshot_send_keys_and_get_text_actions_are_supported()
    {
        var fake = new FakeWindowsAutomation();
        var executor = new BatchExecutor(fake);

        var output = await executor.RunAsync(
            [
                new BatchAction("snapshot", Handle: "w1"),
                new BatchAction("sendKeys", Ref: "w1e1", Chord: "Ctrl+S"),
                new BatchAction("getText", Ref: "w1e2"),
            ],
            stopOnError: true,
            CancellationToken.None);

        Assert.Contains("1. snapshot: ", output, StringComparison.Ordinal);
        Assert.Contains("2. sendKeys: Sent keys", output, StringComparison.Ordinal);
        Assert.Contains("3. getText: ", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unknown_actions_and_missing_fields_are_actionable_errors()
    {
        var executor = new BatchExecutor(new FakeWindowsAutomation());

        var output = await executor.RunAsync(
            [
                new BatchAction("teleport"),
                new BatchAction("click"),
            ],
            stopOnError: true,
            CancellationToken.None);

        Assert.Contains("1. ERROR: INVALID_ARGUMENT: Unknown action 'teleport'", output, StringComparison.Ordinal);
        Assert.Contains("Stopped at action 1 due to error", output, StringComparison.Ordinal);
    }
}
