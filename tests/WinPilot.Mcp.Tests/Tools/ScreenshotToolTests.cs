using ModelContextProtocol.Protocol;
using WinPilot.Automation.Errors;
using WinPilot.Mcp.Tests.Fakes;
using WinPilot.Mcp.Tools;

namespace WinPilot.Mcp.Tests.Tools;

public class ScreenshotToolTests
{
    [Fact]
    public async Task Returns_an_image_content_block()
    {
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 1, 2, 3];
        var fake = new FakeWindowsAutomation
        {
            OnCapture = (_, _) => Task.FromResult(new WinPilot.Automation.Capture.ScreenshotResult(png, null)),
        };
        var tools = new ScreenshotTool(fake);

        var result = await tools.CaptureAsync("w1", null, null, null, null, null, CancellationToken.None);

        Assert.True(result.IsError is null or false);
        var image = Assert.IsType<ImageContentBlock>(result.Content[0]);
        Assert.Equal("image/png", image.MimeType);
        Assert.Equal(png, image.Data);
    }

    [Fact]
    public async Task Adds_a_text_block_when_the_file_was_saved()
    {
        var fake = new FakeWindowsAutomation
        {
            OnCapture = (_, _) => Task.FromResult(new WinPilot.Automation.Capture.ScreenshotResult([1], @"C:\Temp\shot.png")),
        };
        var tools = new ScreenshotTool(fake);

        var result = await tools.CaptureAsync(null, null, null, null, @"C:\Temp\shot.png", null, CancellationToken.None);

        Assert.Equal(2, result.Content.Count);
        Assert.IsType<ImageContentBlock>(result.Content[0]);
        Assert.Equal(@"Saved to C:\Temp\shot.png", Assert.IsType<TextContentBlock>(result.Content[1]).Text);
    }

    [Fact]
    public async Task Forwards_all_capture_options()
    {
        var fake = new FakeWindowsAutomation();
        var tools = new ScreenshotTool(fake);

        _ = await tools.CaptureAsync("w1", null, null, true, null, null, CancellationToken.None);

        Assert.Contains(@"capture:w1||False|True|", fake.Calls);
    }

    [Fact]
    public async Task Maps_engine_validation_errors()
    {
        var fake = new FakeWindowsAutomation
        {
            OnCapture = (_, _) => throw new InvalidArgumentException("background capture requires a window handle."),
        };
        var tools = new ScreenshotTool(fake);

        var result = await tools.CaptureAsync(null, "w1e1", null, true, null, null, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.StartsWith("INVALID_ARGUMENT:", Assert.IsType<TextContentBlock>(result.Content[0]).Text!, StringComparison.Ordinal);
    }
}
