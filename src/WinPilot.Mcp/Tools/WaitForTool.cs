using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using WinPilot.Automation;
using WinPilot.Automation.Elements;
using WinPilot.Automation.Errors;

namespace WinPilot.Mcp.Tools;

/// <summary>Wait-for-element MCP tool.</summary>
[McpServerToolType]
public sealed class WaitForTool(IWindowsAutomation automation)
{
    /// <summary>Waits until an element appears and is usable.</summary>
    [McpServerTool(Name = "windows_wait_for", ReadOnly = true, Idempotent = true)]
    [Description("Wait until an element exists, is enabled, and is on-screen, then return its snapshot line with a fresh ref. " +
                 "Provide either a ref or a selector (name/automationId/controlType).")]
    public async Task<CallToolResult> WaitAsync(
        [Description("Element ref from windows_snapshot to wait for (alternative to a selector).")] string? elementRef = null,
        [Description("Window handle to search in. If omitted, uses the ref's window or the foreground window.")] string? handle = null,
        [Description("Accessible name to wait for (exact match).")] string? name = null,
        [Description("Automation id to wait for (exact match).")] string? automationId = null,
        [Description("Control type to wait for (e.g. Button, Edit, ListItem).")] string? controlType = null,
        [Description("Timeout in milliseconds (default 10000).")] int? timeoutMs = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var hasSelector = !string.IsNullOrEmpty(name) || !string.IsNullOrEmpty(automationId) || !string.IsNullOrEmpty(controlType);
            var selector = hasSelector ? new ElementSelector(name, automationId, controlType) : null;

            var info = await automation.WaitForElementAsync(handle, elementRef, selector, timeoutMs, cancellationToken);
            return ToolErrors.Text($"{info.Line}\nRef: {info.Ref}");
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
