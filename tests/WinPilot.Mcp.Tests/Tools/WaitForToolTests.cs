using ModelContextProtocol.Protocol;
using WinPilot.Automation.Elements;
using WinPilot.Automation.Errors;
using WinPilot.Mcp.Tests.Fakes;
using WinPilot.Mcp.Tools;

namespace WinPilot.Mcp.Tests.Tools;

public class WaitForToolTests
{
    private static string TextOf(CallToolResult result)
        => Assert.IsType<TextContentBlock>(result.Content[0]).Text!;

    [Fact]
    public async Task Waits_by_ref_and_returns_the_line_with_a_fresh_ref()
    {
        var fake = new FakeWindowsAutomation
        {
            OnWaitFor = (_, _, _, _, _) => Task.FromResult(new ElementInfo("w1e7", "button \"Save\" [ref=w1e7]")),
        };
        var tools = new WaitForTool(fake);

        var result = await tools.WaitAsync("w1e5", null, null, null, null, 3000, CancellationToken.None);

        Assert.Equal("button \"Save\" [ref=w1e7]\nRef: w1e7", TextOf(result));
        Assert.Contains("wait:|w1e5||3000", fake.Calls);
    }

    [Fact]
    public async Task Builds_a_selector_from_individual_fields()
    {
        var fake = new FakeWindowsAutomation();
        var tools = new WaitForTool(fake);

        _ = await tools.WaitAsync(null, "w2", "Save", "btnSave", "Button", null, CancellationToken.None);

        Assert.Contains("wait:w2||Save|", fake.Calls);
    }

    [Fact]
    public async Task Passes_no_selector_when_only_a_ref_is_given()
    {
        var fake = new FakeWindowsAutomation();
        var tools = new WaitForTool(fake);

        _ = await tools.WaitAsync("w1e5", null, null, null, null, null, CancellationToken.None);

        Assert.Contains("wait:|w1e5||", fake.Calls);
    }

    [Fact]
    public async Task Maps_timeouts()
    {
        var fake = new FakeWindowsAutomation
        {
            OnWaitFor = (_, _, _, _, _) => throw new OperationTimeoutException("No matching element appeared within 1000 ms."),
        };
        var tools = new WaitForTool(fake);

        var result = await tools.WaitAsync(null, "w1", "Save", null, null, 1000, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.StartsWith("OPERATION_TIMEOUT:", TextOf(result), StringComparison.Ordinal);
    }
}
