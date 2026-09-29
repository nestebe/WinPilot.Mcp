using ModelContextProtocol.Protocol;
using WinPilot.Automation.Errors;
using WinPilot.Mcp.Tests.Fakes;
using WinPilot.Mcp.Tools;

namespace WinPilot.Mcp.Tests.Tools;

public class ElementToolsTests
{
    private static string TextOf(CallToolResult result)
        => Assert.IsType<TextContentBlock>(result.Content[0]).Text!;

    [Fact]
    public async Task Snapshot_returns_the_engine_text_verbatim_and_forwards_parameters()
    {
        var fake = new FakeWindowsAutomation
        {
            OnSnapshot = (handle, maxDepth, _) => Task.FromResult($"- window \"W\" [ref={handle ?? "w1"}] depth={maxDepth}\n"),
        };
        var tools = new ElementTools(fake);

        var result = await tools.SnapshotAsync("w1", 5, CancellationToken.None);

        Assert.Equal("- window \"W\" [ref=w1] depth=5\n", TextOf(result));
        Assert.Contains("snapshot:w1|5", fake.Calls);
    }

    [Fact]
    public async Task Snapshot_without_handle_passes_null_for_the_foreground_window()
    {
        var fake = new FakeWindowsAutomation();
        var tools = new ElementTools(fake);

        _ = await tools.SnapshotAsync(null, null, CancellationToken.None);

        Assert.Contains("snapshot:|", fake.Calls);
    }

    [Fact]
    public async Task Click_forwards_button_defaults()
    {
        var fake = new FakeWindowsAutomation();
        var tools = new ElementTools(fake);

        var result = await tools.ClickAsync("w1e5", null, null, CancellationToken.None);

        Assert.Equal("Invoked w1e5", TextOf(result));
        Assert.Contains("click:w1e5|left|False", fake.Calls);
    }

    [Fact]
    public async Task Click_forwards_right_double_click()
    {
        var fake = new FakeWindowsAutomation();
        var tools = new ElementTools(fake);

        _ = await tools.ClickAsync("w1e5", "right", true, CancellationToken.None);

        Assert.Contains("click:w1e5|right|True", fake.Calls);
    }

    [Fact]
    public async Task Click_maps_stale_ref_errors()
    {
        var fake = new FakeWindowsAutomation
        {
            OnClick = (_, _, _, _) => throw new ElementStaleException(
                "Element ref 'w1e5' is from an older snapshot.",
                "Run windows_snapshot and use the new refs."),
        };
        var tools = new ElementTools(fake);

        var result = await tools.ClickAsync("w1e5", null, null, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.StartsWith("ELEMENT_STALE: Element ref 'w1e5' is from an older snapshot.", TextOf(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Type_forwards_ref_text_and_submit()
    {
        var fake = new FakeWindowsAutomation();
        var tools = new ElementTools(fake);

        var result = await tools.TypeAsync("hello", "w1e5", submit: true, CancellationToken.None);

        Assert.Equal("Typed \"hello\"", TextOf(result));
        Assert.Contains("type:w1e5|hello|True", fake.Calls);
    }

    [Fact]
    public async Task Type_without_ref_targets_the_focused_element()
    {
        var fake = new FakeWindowsAutomation();
        var tools = new ElementTools(fake);

        _ = await tools.TypeAsync("hi", null, false, CancellationToken.None);

        Assert.Contains("type:|hi|False", fake.Calls);
    }

    [Fact]
    public async Task Cancellation_propagates_as_OperationCanceledException()
    {
        var fake = new FakeWindowsAutomation
        {
            OnClick = async (_, _, _, token) =>
            {
                await Task.Delay(Timeout.Infinite, token);
                return "unreachable";
            },
        };
        var tools = new ElementTools(fake);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            tools.ClickAsync("w1e5", null, null, cts.Token));
    }

    [Fact]
    public async Task Fill_forwards_ref_and_value()
    {
        var fake = new FakeWindowsAutomation();
        var tools = new ElementTools(fake);

        var result = await tools.FillAsync("w1e5", "new value", CancellationToken.None);

        Assert.Equal("Filled w1e5 with \"new value\"", TextOf(result));
        Assert.Contains("fill:w1e5|new value", fake.Calls);
    }

    [Fact]
    public async Task Send_keys_forwards_a_chord()
    {
        var fake = new FakeWindowsAutomation();
        var tools = new ElementTools(fake);

        _ = await tools.SendKeysAsync("w1e5", "Ctrl+S", null, CancellationToken.None);

        Assert.Contains("sendkeys:w1e5|Ctrl+S|", fake.Calls);
    }

    [Fact]
    public async Task Send_keys_forwards_a_sequence()
    {
        var fake = new FakeWindowsAutomation();
        var tools = new ElementTools(fake);

        _ = await tools.SendKeysAsync(null, null, ["Ctrl+C", "Down"], CancellationToken.None);

        Assert.Contains("sendkeys:||Ctrl+C,Down", fake.Calls);
    }

    [Fact]
    public async Task Get_text_returns_empty_text_as_success()
    {
        var fake = new FakeWindowsAutomation();
        var tools = new ElementTools(fake);

        var result = await tools.GetTextAsync("w1e5", CancellationToken.None);

        Assert.True(result.IsError is null or false);
        Assert.Equal(string.Empty, TextOf(result));
    }

    [Fact]
    public async Task Get_text_maps_missing_refs()
    {
        var fake = new FakeWindowsAutomation
        {
            OnGetText = (_, _) => throw new ElementNotFoundException("Element not found: w9e9."),
        };
        var tools = new ElementTools(fake);

        var result = await tools.GetTextAsync("w9e9", CancellationToken.None);

        Assert.True(result.IsError);
        Assert.StartsWith("ELEMENT_NOT_FOUND:", TextOf(result), StringComparison.Ordinal);
    }
}
