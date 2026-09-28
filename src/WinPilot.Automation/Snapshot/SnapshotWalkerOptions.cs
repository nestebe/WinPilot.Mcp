namespace WinPilot.Automation.Snapshot;

/// <summary>Limits applied while walking a snapshot tree.</summary>
internal sealed record SnapshotWalkerOptions(int MaxDepth, int MaxNodes, int TimeBudgetMs);
