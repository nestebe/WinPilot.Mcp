using ModelContextProtocol.Protocol;
using WinPilot.Automation.Errors;

namespace WinPilot.Mcp.Tools;

/// <summary>
/// Builds MCP tool results. Engine errors become <c>isError</c> text results with a stable
/// code prefix and an optional actionable hint.
/// </summary>
internal static class ToolErrors
{
    /// <summary>Builds a successful text result.</summary>
    public static CallToolResult Text(string text)
        => new() { Content = [new TextContentBlock { Text = text }] };

    /// <summary>Builds an error text result.</summary>
    public static CallToolResult Error(string message)
        => new() { Content = [new TextContentBlock { Text = message }], IsError = true };

    /// <summary>Maps a typed engine failure to an actionable error result.</summary>
    public static CallToolResult ToErrorResult(WinPilotException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var hint = string.IsNullOrWhiteSpace(exception.Hint) ? string.Empty : $" ({exception.Hint})";
        return Error($"{CodeToText(exception.Code)}: {exception.Message}{hint}");
    }

    /// <summary>Maps an unexpected failure to a generic provider error result.</summary>
    public static CallToolResult ToErrorResult(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return Error(
            $"UI_PROVIDER_ERROR: {exception.Message} (Retry the request; if the problem persists, check the server log.)");
    }

    /// <summary>Formats an error code as SCREAMING_SNAKE_CASE for agent-facing text.</summary>
    public static string CodeToText(WinPilotErrorCode code)
    {
        var name = code.ToString();
        var builder = new System.Text.StringBuilder(name.Length + 4);

        for (var i = 0; i < name.Length; i++)
        {
            if (char.IsUpper(name[i]) && i > 0)
            {
                builder.Append('_');
            }

            builder.Append(char.ToUpperInvariant(name[i]));
        }

        return builder.ToString();
    }
}
