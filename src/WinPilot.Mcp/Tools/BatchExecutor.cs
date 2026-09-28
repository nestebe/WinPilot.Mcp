using WinPilot.Automation;
using WinPilot.Automation.Errors;

namespace WinPilot.Mcp.Tools;

/// <summary>
/// Runs batch actions sequentially through the same engine API as the individual tools.
/// Results are numbered lines matching the original server format.
/// </summary>
internal sealed class BatchExecutor(IWindowsAutomation automation)
{
    /// <summary>Runs the actions and returns one numbered line per action.</summary>
    public async Task<string> RunAsync(IReadOnlyList<BatchAction> actions, bool stopOnError, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actions);

        var results = new List<string>();
        for (var index = 0; index < actions.Count; index++)
        {
            var action = actions[index];
            try
            {
                var output = await ExecuteAsync(action, cancellationToken);
                results.Add($"{index + 1}. {action.Action}: {output}");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (WinPilotException exception)
            {
                results.Add($"{index + 1}. ERROR: {ToolErrors.FormatError(exception)}");
                if (stopOnError)
                {
                    results.Add($"Stopped at action {index + 1} due to error");
                    break;
                }
            }
        }

        return string.Join('\n', results);
    }

    private Task<string> ExecuteAsync(BatchAction action, CancellationToken cancellationToken) => action.Action switch
    {
        "click" => automation.ClickAsync(RequireRef(action), "left", false, cancellationToken),
        "type" => automation.TypeAsync(action.Ref, Require(action.Text, "type", "text"), false, cancellationToken),
        "fill" => automation.FillAsync(RequireRef(action), Require(action.Value, "fill", "value"), cancellationToken),
        "wait" => WaitAsync(action.Ms ?? 100, cancellationToken),
        "snapshot" => automation.SnapshotAsync(action.Handle, null, cancellationToken),
        "sendKeys" => automation.SendKeysAsync(action.Ref, action.Chord, action.Keys, cancellationToken),
        "getText" => automation.GetTextAsync(RequireRef(action), cancellationToken),
        _ => throw new InvalidArgumentException(
            $"Unknown action '{action.Action}'.",
            "Supported actions: click, type, fill, wait, snapshot, sendKeys, getText."),
    };

    private static async Task<string> WaitAsync(int milliseconds, CancellationToken cancellationToken)
    {
        await Task.Delay(milliseconds, cancellationToken);
        return $"Waited {milliseconds}ms";
    }

    private static string RequireRef(BatchAction action)
        => Require(action.Ref, action.Action, "ref");

    private static string Require(string? value, string action, string field)
        => string.IsNullOrEmpty(value)
            ? throw new InvalidArgumentException($"Action '{action}' requires '{field}'.")
            : value;
}
