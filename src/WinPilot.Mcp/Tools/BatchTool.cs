using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using WinPilot.Automation;
using WinPilot.Automation.Errors;

namespace WinPilot.Mcp.Tools;

/// <summary>Batch MCP tool: multiple actions in one call.</summary>
[McpServerToolType]
public sealed class BatchTool(IWindowsAutomation automation)
{
    /// <summary>Runs a list of actions sequentially.</summary>
    [McpServerTool(Name = "windows_batch", Destructive = true)]
    [Description("Execute multiple actions in a single call, much faster than individual calls. " +
                 "Actions run in order: click, type, fill, wait, snapshot, sendKeys, getText. Returns one result line per action.")]
    public async Task<CallToolResult> RunAsync(
        [Description("List of actions to execute in order.")] IReadOnlyList<BatchAction> actions,
        [Description("Stop executing when an action fails (default true).")] bool stopOnError = true,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var executor = new BatchExecutor(automation);
            return ToolErrors.Text(await executor.RunAsync(actions, stopOnError, cancellationToken));
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
