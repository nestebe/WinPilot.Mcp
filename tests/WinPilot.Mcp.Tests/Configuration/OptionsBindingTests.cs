using WinPilot.Mcp.Configuration;

namespace WinPilot.Mcp.Tests.Configuration;

public class OptionsBindingTests
{
    [Theory]
    [InlineData("OPERATION_TIMEOUT_SECONDS", "OperationTimeoutSeconds")]
    [InlineData("SNAPSHOT_TIME_BUDGET_MS", "SnapshotTimeBudgetMs")]
    [InlineData("KEEP_APPS_ON_EXIT", "KeepAppsOnExit")]
    [InlineData("LOG_FILE", "LogFile")]
    [InlineData("SNAPSHOT_MAX_DEPTH", "SnapshotMaxDepth")]
    public void Translates_snake_case_to_pascal_case(string envKey, string expected)
        => Assert.Equal(expected, OptionsBinding.EnvKeyToConfigurationKey(envKey));

    [Fact]
    public void Translate_environment_collects_prefixed_variables_case_insensitively()
    {
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["WINPILOT_OPERATION_TIMEOUT_SECONDS"] = "60",
            ["winpilot_keep_apps_on_exit"] = "true",
            ["PATH"] = "C:\\Windows",
            ["WINPILOT_"] = "ignored",
        };

        var translated = OptionsBinding.TranslateEnvironment(environment);

        Assert.Equal("60", translated["OperationTimeoutSeconds"]);
        Assert.Equal("true", translated["KeepAppsOnExit"]);
        Assert.False(translated.ContainsKey("Path"));
        Assert.Equal(2, translated.Count);
    }
}
