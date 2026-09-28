namespace WinPilot.Automation.Configuration;

/// <summary>
/// Runtime configuration for the WinPilot automation engine and MCP host.
/// Bound from the <c>WinPilot</c> configuration section, <c>appsettings.json</c>,
/// and <c>WINPILOT_</c>-prefixed environment variables.
/// </summary>
public sealed class WinPilotOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "WinPilot";

    /// <summary>Gets or sets the soft operation timeout, in seconds. Default: 30.</summary>
    public int OperationTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Gets or sets the hard timeout, in seconds. When the worker has been busy longer than this,
    /// the next request recycles the worker and its UI Automation context. Default: 90.
    /// </summary>
    public int HardTimeoutSeconds { get; set; } = 90;

    /// <summary>Gets or sets the default maximum snapshot depth. Default: 10.</summary>
    public int SnapshotMaxDepth { get; set; } = 10;

    /// <summary>Gets or sets the maximum number of snapshot nodes before truncation. Default: 2000.</summary>
    public int SnapshotMaxNodes { get; set; } = 2000;

    /// <summary>Gets or sets the snapshot time budget, in milliseconds. Default: 10000.</summary>
    public int SnapshotTimeBudgetMs { get; set; } = 10_000;

    /// <summary>Gets or sets the timeout for waiting for a launched app window, in milliseconds. Default: 10000.</summary>
    public int LaunchWindowTimeoutMs { get; set; } = 10_000;

    /// <summary>Gets or sets the timeout for a graceful window close, in milliseconds. Default: 5000.</summary>
    public int CloseWindowTimeoutMs { get; set; } = 5_000;

    /// <summary>Gets or sets the default wait-for-element timeout, in milliseconds. Default: 10000.</summary>
    public int WaitForElementTimeoutMs { get; set; } = 10_000;

    /// <summary>Gets or sets a value indicating whether apps launched by this server stay alive on shutdown. Default: false.</summary>
    public bool KeepAppsOnExit { get; set; }

    /// <summary>
    /// Validates the configuration values.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">A value is not positive, or the hard timeout does not exceed the soft timeout.</exception>
    public void Validate()
    {
        EnsurePositive(OperationTimeoutSeconds, nameof(OperationTimeoutSeconds));
        EnsurePositive(HardTimeoutSeconds, nameof(HardTimeoutSeconds));
        EnsurePositive(SnapshotMaxDepth, nameof(SnapshotMaxDepth));
        EnsurePositive(SnapshotMaxNodes, nameof(SnapshotMaxNodes));
        EnsurePositive(SnapshotTimeBudgetMs, nameof(SnapshotTimeBudgetMs));
        EnsurePositive(LaunchWindowTimeoutMs, nameof(LaunchWindowTimeoutMs));
        EnsurePositive(CloseWindowTimeoutMs, nameof(CloseWindowTimeoutMs));
        EnsurePositive(WaitForElementTimeoutMs, nameof(WaitForElementTimeoutMs));

        if (HardTimeoutSeconds <= OperationTimeoutSeconds)
        {
            throw new ArgumentOutOfRangeException(
                nameof(HardTimeoutSeconds),
                HardTimeoutSeconds,
                $"{nameof(HardTimeoutSeconds)} must be greater than {nameof(OperationTimeoutSeconds)}.");
        }
    }

    private static void EnsurePositive(int value, string propertyName)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(propertyName, value, $"{propertyName} must be greater than zero.");
        }
    }
}
