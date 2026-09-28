using System.Text.RegularExpressions;
using WinPilot.Automation.Errors;
using Xunit;

namespace WinPilot.IntegrationTests;

/// <summary>Helpers for working with snapshot text in integration tests.</summary>
internal static partial class TestHelpers
{
    /// <summary>Finds the ref of the first snapshot line whose quoted name matches exactly.</summary>
    public static string FindRef(string snapshot, string name)
    {
        var match = RefPattern().Match(snapshot, 0);
        while (match.Success)
        {
            if (string.Equals(match.Groups["name"].Value, name, StringComparison.Ordinal))
            {
                return match.Groups["ref"].Value;
            }

            match = match.NextMatch();
        }

        Assert.Fail($"No ref for '{name}' found in snapshot:\n{snapshot}");
        return string.Empty;
    }

    /// <summary>Observes a fire-and-forget operation, swallowing expected engine failures.</summary>
    public static async Task ObserveAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (WinPilotException)
        {
        }
        catch (OperationCanceledException)
        {
        }
    }

    [GeneratedRegex("""- [\w]+ "(?<name>[^"]*)" \[ref=(?<ref>w\d+e\d+)\]""")]
    private static partial Regex RefPattern();
}
