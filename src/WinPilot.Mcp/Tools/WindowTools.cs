using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using WinPilot.Automation;
using WinPilot.Automation.Errors;

namespace WinPilot.Mcp.Tools;

/// <summary>Window-level MCP tools: launch, list, focus, close.</summary>
[McpServerToolType]
public sealed class WindowTools(IWindowsAutomation automation)
{
    /// <summary>Launches a Windows application and returns its window handle.</summary>
    [McpServerTool(Name = "windows_launch", Destructive = true, OpenWorld = true)]
    [Description("Launch a Windows application and return a window handle (w1) for use with the other tools.")]
    public async Task<CallToolResult> LaunchAsync(
        [Description("Executable path or UWP app id, e.g. 'calc.exe' or 'Microsoft.WindowsCalculator_8wekyb3d8bbwe!App'.")] string app,
        [Description("Optional command line arguments.")] IReadOnlyList<string>? args = null,
        [Description("Optional timeout in milliseconds to wait for the app window (default 10000).")] int? timeoutMs = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var window = await automation.LaunchAsync(app, args, timeoutMs, cancellationToken);
            return ToolErrors.Text($"Launched {app}\nWindow handle: {window.Handle}\nTitle: {window.Title}");
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

    /// <summary>Lists all open windows.</summary>
    [McpServerTool(Name = "windows_list_windows", ReadOnly = true, Idempotent = true)]
    [Description("List all open windows with their handles, titles and process names. Use this to find windows to interact with.")]
    public async Task<CallToolResult> ListAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var windows = await automation.ListWindowsAsync(cancellationToken);
            if (windows.Count == 0)
            {
                return ToolErrors.Text("No windows found");
            }

            var lines = windows.Select(window => $"- {window.Handle}: \"{window.Title}\" ({window.ProcessName ?? "unknown"})");
            return ToolErrors.Text(string.Join('\n', lines));
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

    /// <summary>Brings a window to the foreground.</summary>
    [McpServerTool(Name = "windows_focus", Idempotent = true)]
    [Description("Bring a window to the foreground and give it focus, by handle or by title substring.")]
    public async Task<CallToolResult> FocusAsync(
        [Description("Window handle from windows_list_windows or windows_launch.")] string? handle = null,
        [Description("Window title substring (alternative to handle).")] string? title = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var window = await automation.FocusWindowAsync(handle, title, cancellationToken);
            return ToolErrors.Text($"Focused window {window.Handle} \"{window.Title}\"");
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

    /// <summary>Closes a window, optionally killing the process of apps launched by this server.</summary>
    [McpServerTool(Name = "windows_close", Destructive = true)]
    [Description("Close a window gracefully (WM_CLOSE). With force: true, an app launched by this server is killed if it does not close in time.")]
    public async Task<CallToolResult> CloseAsync(
        [Description("Window handle to close.")] string handle,
        [Description("Kill the app process when it was launched by this server and the graceful close times out.")] bool force = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await automation.CloseWindowAsync(handle, force, cancellationToken);
            return ToolErrors.Text($"Closed window {handle}");
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
