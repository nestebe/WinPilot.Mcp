using System.Text;

namespace WinPilot.Automation.Snapshot;

/// <summary>
/// Renders a snapshot tree as agent-friendly text. Pure and deterministic: no UI Automation access.
/// </summary>
/// <remarks>
/// Line format: <c>- role "name" [ref=w1e5] [state]</c>, two spaces of indentation per level,
/// one line per node, LF line endings. Element names are escaped so each node stays on one line:
/// backslash to <c>\\</c>, quote to <c>\"</c>, newline to <c>\n</c>, carriage returns are dropped.
/// </remarks>
internal static class SnapshotFormatter
{
    /// <summary>Renders the tree; children at <paramref name="maxDepth"/> are replaced by a depth-limit marker.</summary>
    public static string Format(SnapshotNode root, int maxDepth)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxDepth, 1);

        var builder = new StringBuilder();
        AppendNode(builder, root, 0, maxDepth);
        return builder.ToString();
    }

    private static void AppendNode(StringBuilder builder, SnapshotNode node, int depth, int maxDepth)
    {
        var indent = new string(' ', depth * 2);

        if (node.Truncation is { } truncation)
        {
            builder.Append(indent).Append("- ... (truncated: ").Append(GetTruncationLabel(truncation)).Append(")\n");
            return;
        }

        builder.Append(indent).Append("- ").Append(node.Role);
        if (!string.IsNullOrEmpty(node.Name))
        {
            builder.Append(" \"").Append(EscapeName(node.Name)).Append('"');
        }

        builder.Append(" [ref=").Append(node.Ref).Append(']');

        foreach (var state in node.States)
        {
            builder.Append(" [").Append(state).Append(']');
        }

        builder.Append('\n');

        if (depth >= maxDepth)
        {
            if (node.Children.Count > 0)
            {
                AppendNode(builder, SnapshotNode.Truncated(SnapshotTruncation.DepthLimit), depth + 1, maxDepth);
            }

            return;
        }

        foreach (var child in node.Children)
        {
            AppendNode(builder, child, depth + 1, maxDepth);
        }
    }

    private static string GetTruncationLabel(SnapshotTruncation truncation) => truncation switch
    {
        SnapshotTruncation.DepthLimit => "depth limit",
        SnapshotTruncation.NodeLimit => "node limit reached",
        SnapshotTruncation.TimeBudget => "time budget exceeded",
        _ => "unknown",
    };

    private static string EscapeName(string name)
        => name
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
}
