using WinPilot.Automation.Snapshot;

namespace WinPilot.Automation.Tests.Snapshot;

public class SnapshotFormatterTests
{
    [Fact]
    public void Formats_exact_original_format()
    {
        var tree = Node("window", "Calculator", "w1", Node("button", "Seven", "w1e4", states: ["disabled"]));

        var text = SnapshotFormatter.Format(tree, 10);

        Assert.Equal("- window \"Calculator\" [ref=w1]\n  - button \"Seven\" [ref=w1e4] [disabled]\n", text);
    }

    [Fact]
    public void Escapes_quotes_backslashes_and_newlines_onto_one_line()
    {
        var tree = Node("text", "a\"b\\c\nd\re", "w1e1");

        var text = SnapshotFormatter.Format(tree, 10);

        Assert.Equal("- text \"a\\\"b\\\\c\\nde\" [ref=w1e1]\n", text);
    }

    [Fact]
    public void Omits_name_when_null_or_empty()
    {
        Assert.Equal("- group [ref=w1e2]\n", SnapshotFormatter.Format(Node("group", null, "w1e2"), 10));
        Assert.Equal("- group [ref=w1e2]\n", SnapshotFormatter.Format(Node("group", "", "w1e2"), 10));
    }

    [Fact]
    public void Renders_multiple_states_in_the_given_order()
    {
        var tree = Node("checkbox", "Option", "w1e3", states: ["disabled", "checked"]);

        Assert.Equal("- checkbox \"Option\" [ref=w1e3] [disabled] [checked]\n", SnapshotFormatter.Format(tree, 10));
    }

    [Fact]
    public void Depth_limit_marker_is_rendered_indented_under_the_cut_parent()
    {
        var tree = Node("window", "W", "w1", Node("group", "G", "w1e1", Node("button", "B", "w1e2")));

        var text = SnapshotFormatter.Format(tree, maxDepth: 1);

        Assert.Equal(
            "- window \"W\" [ref=w1]\n  - group \"G\" [ref=w1e1]\n    - ... (truncated: depth limit)\n",
            text);
    }

    [Fact]
    public void Truncation_nodes_render_in_place()
    {
        var tree = Node(
            "window", "W", "w1",
            Node("button", "B", "w1e1"),
            SnapshotNode.Truncated(SnapshotTruncation.NodeLimit),
            SnapshotNode.Truncated(SnapshotTruncation.TimeBudget));

        var text = SnapshotFormatter.Format(tree, 10);

        Assert.Equal(
            "- window \"W\" [ref=w1]\n  - button \"B\" [ref=w1e1]\n  - ... (truncated: node limit reached)\n  - ... (truncated: time budget exceeded)\n",
            text);
    }

    private static SnapshotNode Node(string role, string? name, string refId, params SnapshotNode[] children)
        => new(role, name, refId, [], children);

    private static SnapshotNode Node(string role, string? name, string refId, IReadOnlyList<string> states, params SnapshotNode[] children)
        => new(role, name, refId, states, children);
}
