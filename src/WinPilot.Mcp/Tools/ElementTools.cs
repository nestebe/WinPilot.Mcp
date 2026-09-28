using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using WinPilot.Automation;
using WinPilot.Automation.Errors;

namespace WinPilot.Mcp.Tools;

/// <summary>Element-level MCP tools: snapshot, click, type, fill, send keys, get text.</summary>
[McpServerToolType]
public sealed class ElementTools(IWindowsAutomation automation)
{
    /// <summary>Captures the accessibility snapshot of a window.</summary>
    [McpServerTool(Name = "windows_snapshot", ReadOnly = true, Idempotent = true)]
    [Description("Capture the accessibility tree of a window as text with element refs (for example w1e5). " +
                 "Use the refs with windows_click/type/fill/send_keys/get_text. Run it before interacting with elements.")]
    public async Task<CallToolResult> SnapshotAsync(
        [Description("Window handle from windows_launch or windows_list_windows. If omitted, uses the foreground window.")] string? handle = null,
        [Description("Optional maximum tree depth (1-20, default 10). Deeper subtrees are replaced by truncation markers.")] int? maxDepth = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return ToolErrors.Text(await automation.SnapshotAsync(handle, maxDepth, cancellationToken));
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

    /// <summary>Clicks an element by ref.</summary>
    [McpServerTool(Name = "windows_click")]
    [Description("Click an element by its ref (from windows_snapshot). Prefers the Invoke pattern for reliability, falls back to a mouse click.")]
    public async Task<CallToolResult> ClickAsync(
        [Description("Element ref from windows_snapshot (e.g. 'w1e5').")] string elementRef,
        [Description("Mouse button: left (default), right, middle. Right and middle always use a real mouse click.")] string? button = null,
        [Description("Double-click instead of a single click (default false).")] bool? doubleClick = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var message = await automation.ClickAsync(elementRef, button ?? "left", doubleClick ?? false, cancellationToken);
            return ToolErrors.Text(message);
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

    /// <summary>Types text into an element or the focused element.</summary>
    [McpServerTool(Name = "windows_type")]
    [Description("Type text into an element (focused first when a ref is given) or into the focused element. " +
                 "Use windows_fill to replace existing content.")]
    public async Task<CallToolResult> TypeAsync(
        [Description("Element ref from windows_snapshot. If omitted, types into the currently focused element.")] string? elementRef,
        [Description("Text to type.")] string text,
        [Description("Press Enter after typing (default false).")] bool submit = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return ToolErrors.Text(await automation.TypeAsync(elementRef, text, submit, cancellationToken));
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

    /// <summary>Clears and fills a text element.</summary>
    [McpServerTool(Name = "windows_fill")]
    [Description("Clear and fill a text field with a new value. Prefers the Value pattern for reliability.")]
    public async Task<CallToolResult> FillAsync(
        [Description("Element ref from windows_snapshot (e.g. 'w1e5').")] string elementRef,
        [Description("Value to fill.")] string value,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return ToolErrors.Text(await automation.FillAsync(elementRef, value, cancellationToken));
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

    /// <summary>Sends key presses or chords.</summary>
    [McpServerTool(Name = "windows_send_keys")]
    [Description("Send one key chord or a sequence of chords to an element (focused first) or the focused element. " +
                 "Provide exactly one of chord ('Ctrl+Right') or keys (['Ctrl+C', 'Down']).")]
    public async Task<CallToolResult> SendKeysAsync(
        [Description("Element ref from windows_snapshot. If omitted, sends to the focused element.")] string? elementRef = null,
        [Description("Single key chord, e.g. 'Ctrl+Right' or 'Alt+F4'.")] string? chord = null,
        [Description("Sequence of key presses/chords, e.g. ['Ctrl+C', 'Ctrl+V', 'Enter'].")] IReadOnlyList<string>? keys = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return ToolErrors.Text(await automation.SendKeysAsync(elementRef, chord, keys, cancellationToken));
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

    /// <summary>Reads the text content of an element.</summary>
    [McpServerTool(Name = "windows_get_text", ReadOnly = true, Idempotent = true)]
    [Description("Get the text content of an element. Returns the element's Name property, or for text inputs, the current value.")]
    public async Task<CallToolResult> GetTextAsync(
        [Description("Element ref from windows_snapshot (e.g. 'w1e5').")] string elementRef,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return ToolErrors.Text(await automation.GetTextAsync(elementRef, cancellationToken));
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
