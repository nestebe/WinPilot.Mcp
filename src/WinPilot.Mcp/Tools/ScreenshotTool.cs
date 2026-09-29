using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using WinPilot.Automation;
using WinPilot.Automation.Capture;
using WinPilot.Automation.Errors;

namespace WinPilot.Mcp.Tools;

/// <summary>Screenshot MCP tool.</summary>
[McpServerToolType]
public sealed class ScreenshotTool(IWindowsAutomation automation)
{
    /// <summary>Captures a screenshot of the screen, a window, or an element.</summary>
    [McpServerTool(Name = "windows_screenshot", ReadOnly = true)]
    [Description("Take a screenshot of a window, an element, or the whole screen and return it as a PNG image. " +
                 "Use it for visual verification when the accessibility tree is not enough.")]
    public async Task<CallToolResult> CaptureAsync(
        [Description("Window handle. If omitted, captures the foreground window.")] string? handle = null,
        [Description("Element ref to capture. If omitted, captures the whole window.")] string? @ref = null,
        [Description("Capture the entire screen (default false).")] bool? fullScreen = null,
        [Description("Use native background window capture for a window handle, falling back to a normal capture if unavailable (default false).")] bool? background = null,
        [Description("Absolute local .png file path to save the screenshot to.")] string? savePath = null,
        [Description("Allow savePath to replace an existing file (default false).")] bool? overwrite = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await automation.CaptureAsync(
                new CaptureRequest(handle, @ref, fullScreen ?? false, background ?? false, savePath, overwrite ?? false),
                cancellationToken);

            var content = new List<ContentBlock>
            {
                new ImageContentBlock { Data = result.Png, MimeType = "image/png" },
            };

            if (result.SavedPath is not null)
            {
                content.Add(new TextContentBlock { Text = $"Saved to {result.SavedPath}" });
            }

            return new CallToolResult { Content = content };
        }
        catch (WinPilotException exception)
        {
            return ToolErrors.ToErrorResult(exception);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return ToolErrors.ToErrorResult(exception);
        }
    }
}
