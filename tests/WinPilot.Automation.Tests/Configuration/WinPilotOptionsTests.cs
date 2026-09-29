using WinPilot.Automation.Configuration;

namespace WinPilot.Automation.Tests.Configuration;

public class WinPilotOptionsTests
{
    [Fact]
    public void Defaults_match_spec()
    {
        var options = new WinPilotOptions();

        Assert.Equal(30, options.OperationTimeoutSeconds);
        Assert.Equal(90, options.HardTimeoutSeconds);
        Assert.Equal(10, options.SnapshotMaxDepth);
        Assert.Equal(2000, options.SnapshotMaxNodes);
        Assert.Equal(10_000, options.SnapshotTimeBudgetMs);
        Assert.Equal(15_000, options.LaunchWindowTimeoutMs);
        Assert.Equal(5_000, options.CloseWindowTimeoutMs);
        Assert.Equal(10_000, options.WaitForElementTimeoutMs);
        Assert.False(options.KeepAppsOnExit);

        options.Validate();
    }

    [Theory]
    [InlineData(0, 90, 10, 2000, 10000, 10000)]
    [InlineData(30, 0, 10, 2000, 10000, 10000)]
    [InlineData(30, 90, 0, 2000, 10000, 10000)]
    [InlineData(30, 90, 10, 0, 10000, 10000)]
    [InlineData(30, 90, 10, 2000, 0, 10000)]
    [InlineData(30, 90, 10, 2000, 10000, 0)]
    public void Non_positive_values_are_rejected(int operation, int hard, int depth, int nodes, int budget, int launch)
    {
        var options = new WinPilotOptions
        {
            OperationTimeoutSeconds = operation,
            HardTimeoutSeconds = hard,
            SnapshotMaxDepth = depth,
            SnapshotMaxNodes = nodes,
            SnapshotTimeBudgetMs = budget,
            LaunchWindowTimeoutMs = launch,
        };

        Assert.Throws<ArgumentOutOfRangeException>(options.Validate);
    }

    [Fact]
    public void Hard_timeout_must_exceed_operation_timeout()
    {
        var options = new WinPilotOptions { OperationTimeoutSeconds = 90, HardTimeoutSeconds = 30 };

        Assert.Throws<ArgumentOutOfRangeException>(options.Validate);
    }
}
