using System.Text;

namespace WinPilot.Mcp.Configuration;

/// <summary>
/// Maps <c>WINPILOT_</c>-prefixed environment variables in SNAKE_CASE to PascalCase
/// configuration keys that bind to <see cref="WinPilot.Automation.Configuration.WinPilotOptions"/>.
/// </summary>
internal static class OptionsBinding
{
    /// <summary>Environment variable prefix for WinPilot settings.</summary>
    public const string Prefix = "WINPILOT_";

    /// <summary>Translates <c>OPERATION_TIMEOUT_SECONDS</c> to <c>OperationTimeoutSeconds</c>.</summary>
    public static string EnvKeyToConfigurationKey(string envKey)
    {
        ArgumentException.ThrowIfNullOrEmpty(envKey);

        var builder = new StringBuilder(envKey.Length);
        foreach (var part in envKey.Split('_', StringSplitOptions.RemoveEmptyEntries))
        {
            builder.Append(char.ToUpperInvariant(part[0]));
            if (part.Length > 1)
            {
                builder.Append(part.AsSpan(1).ToString().ToLowerInvariant());
            }
        }

        return builder.ToString();
    }

    /// <summary>Collects prefixed variables (prefix stripped, name translated) as configuration pairs.</summary>
    public static IReadOnlyDictionary<string, string?> TranslateEnvironment(
        IEnumerable<KeyValuePair<string, string?>> environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        var translated = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in environment)
        {
            if (key.Length <= Prefix.Length || !key.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            translated[EnvKeyToConfigurationKey(key[Prefix.Length..])] = value;
        }

        return translated;
    }
}
