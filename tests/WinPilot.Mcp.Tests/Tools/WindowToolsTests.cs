using ModelContextProtocol.Protocol;
using WinPilot.Automation.Errors;
using WinPilot.Automation.Windows;
using WinPilot.Mcp.Tests.Fakes;
using WinPilot.Mcp.Tools;

namespace WinPilot.Mcp.Tests.Tools;

public class WindowToolsTests
{
    private static string TextOf(CallToolResult result)
        => Assert.IsType<TextContentBlock>(result.Content[0]).Text!;

    [Fact]
    public async Task Launch_returns_the_window_handle_and_title()
    {
        var fake = new FakeWindowsAutomation();
        var tools = new WindowTools(fake);

        var result = await tools.LaunchAsync("calc.exe", ["--x"], 5000, CancellationToken.None);

        Assert.True(result.IsError is null or false);
        Assert.Equal("Launched calc.exe\nWindow handle: w1\nTitle: Test App", TextOf(result));
        Assert.Contains("launch:calc.exe|--x|5000", fake.Calls);
    }

    [Fact]
    public async Task Launch_maps_engine_failures_to_an_error_result()
    {
        var fake = new FakeWindowsAutomation
        {
            OnLaunch = (_, _, _, _) => throw new LaunchFailedException("No window appeared.", "Retry."),
        };
        var tools = new WindowTools(fake);

        var result = await tools.LaunchAsync("ghost.exe", null, null, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.StartsWith("LAUNCH_FAILED: No window appeared.", TextOf(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task List_windows_formats_one_line_per_window()
    {
        var fake = new FakeWindowsAutomation
        {
            Windows =
            [
                new WindowInfo("w1", new IntPtr(1), "Calculator", 42, "calc"),
                new WindowInfo("w2", new IntPtr(2), "Untitled - Notepad", 43, null),
            ],
        };
        var tools = new WindowTools(fake);

        var result = await tools.ListAsync(CancellationToken.None);

        Assert.Equal("- w1: \"Calculator\" (calc)\n- w2: \"Untitled - Notepad\" (unknown)", TextOf(result));
    }

    [Fact]
    public async Task List_windows_reports_an_empty_desktop()
    {
        var tools = new WindowTools(new FakeWindowsAutomation());

        var result = await tools.ListAsync(CancellationToken.None);

        Assert.Equal("No windows found", TextOf(result));
    }

    [Fact]
    public async Task Focus_requires_a_target()
    {
        var tools = new WindowTools(new FakeWindowsAutomation());

        var result = await tools.FocusAsync(null, null, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.StartsWith("INVALID_ARGUMENT:", TextOf(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Focus_returns_the_focused_window()
    {
        var fake = new FakeWindowsAutomation();
        var tools = new WindowTools(fake);

        var result = await tools.FocusAsync(null, "calc", CancellationToken.None);

        Assert.Equal("Focused window w1 \"Test App\"", TextOf(result));
        Assert.Contains("focus:|calc", fake.Calls);
    }

    [Fact]
    public async Task Close_forwards_the_force_flag()
    {
        var fake = new FakeWindowsAutomation();
        var tools = new WindowTools(fake);

        var result = await tools.CloseAsync("w1", force: true, CancellationToken.None);

        Assert.Equal("Closed window w1", TextOf(result));
        Assert.Contains("close:w1|True", fake.Calls);
    }
}
