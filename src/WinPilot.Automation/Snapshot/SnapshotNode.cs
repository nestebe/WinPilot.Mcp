namespace WinPilot.Automation.Snapshot;

/// <summary>Reason a snapshot subtree was cut short.</summary>
internal enum SnapshotTruncation
{
    /// <summary>The maximum depth was reached.</summary>
    DepthLimit,

    /// <summary>The maximum node count was reached.</summary>
    NodeLimit,

    /// <summary>The snapshot time budget was exceeded.</summary>
    TimeBudget,
}

/// <summary>
/// A snapshot tree node: role, optional name, reference, state indicators, and children.
/// A node carrying <see cref="Truncation"/> renders as an explicit truncation marker.
/// </summary>
internal sealed record SnapshotNode(
    string Role,
    string? Name,
    string Ref,
    IReadOnlyList<string> States,
    IReadOnlyList<SnapshotNode> Children,
    SnapshotTruncation? Truncation = null)
{
    /// <summary>Creates a marker node that renders as <c>- ... (truncated: ...)</c>.</summary>
    public static SnapshotNode Truncated(SnapshotTruncation kind) => new("...", null, string.Empty, [], [], kind);
}
