using WinPilot.Automation.Elements;
using WinPilot.Automation.Snapshot;

namespace WinPilot.Automation.Tests.Snapshot;

public class SnapshotWalkerTests
{
    private static SnapshotWalker CreateWalker(int maxDepth = 10, int maxNodes = 2000, int timeBudgetMs = 10_000)
        => new(new SnapshotWalkerOptions(maxDepth, maxNodes, timeBudgetMs));

    private static Func<IUiNode, string> RefFactory(out Func<int> refCount)
    {
        var counter = 0;
        refCount = () => counter;
        return _ => ElementRef.For("w1", ++counter);
    }

    [Fact]
    public void Walks_tree_with_roles_states_and_refs()
    {
        var tree = new FakeNode
        {
            ControlTypeName = "Window",
            Name = "Calculator",
            Children =
            {
                new FakeNode { ControlTypeName = "Button", Name = "Seven", IsEnabled = false },
                new FakeNode { ControlTypeName = "Edit", Name = "Display", IsReadOnly = true },
                new FakeNode { ControlTypeName = "CheckBox", Name = "Option", ToggleState = "On" },
            },
        };

        var node = CreateWalker().Walk("w1", tree, RefFactory(out var count), CancellationToken.None);

        var text = SnapshotFormatter.Format(node, 10);
        Assert.Equal(
            "- window \"Calculator\" [ref=w1e1]\n" +
            "  - button \"Seven\" [ref=w1e2] [disabled]\n" +
            "  - textbox \"Display\" [ref=w1e3] [readonly]\n" +
            "  - checkbox \"Option\" [ref=w1e4] [checked]\n",
            text);
        Assert.Equal(4, count());
    }

    [Fact]
    public void Drops_unnamed_noise_subtrees()
    {
        var tree = new FakeNode
        {
            ControlTypeName = "Window",
            Name = "W",
            Children =
            {
                new FakeNode
                {
                    ControlTypeName = "Separator",
                    Children = { new FakeNode { ControlTypeName = "Button", Name = "Hidden" } },
                },
                new FakeNode { ControlTypeName = "Button", Name = "Visible" },
            },
        };

        var node = CreateWalker().Walk("w1", tree, RefFactory(out _), CancellationToken.None);

        var text = SnapshotFormatter.Format(node, 10);
        Assert.DoesNotContain("Hidden", text, StringComparison.Ordinal);
        Assert.Contains("Visible", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Falls_back_to_bracketed_automation_id_when_name_is_empty()
    {
        var tree = new FakeNode
        {
            ControlTypeName = "Window",
            Name = "W",
            Children = { new FakeNode { ControlTypeName = "Button", Name = null, AutomationId = "btnHello" } },
        };

        var node = CreateWalker().Walk("w1", tree, RefFactory(out _), CancellationToken.None);

        Assert.Contains("button \"[btnHello]\"", SnapshotFormatter.Format(node, 10), StringComparison.Ordinal);
    }

    [Fact]
    public void Adds_depth_limit_marker_when_the_cut_node_has_children()
    {
        var tree = new FakeNode
        {
            ControlTypeName = "Window",
            Name = "W",
            Children =
            {
                new FakeNode
                {
                    ControlTypeName = "Group",
                    Name = "G",
                    Children = { new FakeNode { ControlTypeName = "Button", Name = "Deep" } },
                },
            },
        };

        var node = CreateWalker(maxDepth: 1).Walk("w1", tree, RefFactory(out var count), CancellationToken.None);

        Assert.Equal(
            "- window \"W\" [ref=w1e1]\n  - group \"G\" [ref=w1e2]\n    - ... (truncated: depth limit)\n",
            SnapshotFormatter.Format(node, 10));
        Assert.Equal(2, count());
    }

    [Fact]
    public void Adds_node_limit_marker_and_stops()
    {
        var tree = new FakeNode
        {
            ControlTypeName = "Window",
            Name = "W",
            Children =
            {
                new FakeNode { ControlTypeName = "Button", Name = "A" },
                new FakeNode { ControlTypeName = "Button", Name = "B" },
                new FakeNode { ControlTypeName = "Button", Name = "C" },
            },
        };

        var node = CreateWalker(maxNodes: 2).Walk("w1", tree, RefFactory(out var count), CancellationToken.None);

        var text = SnapshotFormatter.Format(node, 10);
        Assert.Contains("(truncated: node limit reached)", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\"B\"", text, StringComparison.Ordinal);
        Assert.Equal(2, count());
    }

    [Fact]
    public void Adds_time_budget_marker_when_the_budget_is_exhausted()
    {
        var slowChild = new SlowChildrenNode
        {
            ControlTypeName = "Group",
            Name = "Slow",
            Children = { new FakeNode { ControlTypeName = "Button", Name = "Inner" } },
        };
        var tree = new FakeNode
        {
            ControlTypeName = "Window",
            Name = "W",
            Children = { slowChild, new FakeNode { ControlTypeName = "Button", Name = "Never" } },
        };

        var node = CreateWalker(timeBudgetMs: 5).Walk("w1", tree, RefFactory(out _), CancellationToken.None);

        var text = SnapshotFormatter.Format(node, 10);
        Assert.Contains("(truncated: time budget exceeded)", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Never", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Survives_hostile_property_getters()
    {
        var tree = new FakeNode
        {
            ControlTypeName = "Window",
            Name = "W",
            Children = { new HostileNode() },
        };

        var node = CreateWalker().Walk("w1", tree, RefFactory(out _), CancellationToken.None);

        var text = SnapshotFormatter.Format(node, 10);
        Assert.Contains("[ref=w1e2]", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Cancellation_is_checked_during_the_walk()
    {
        var tree = new FakeNode
        {
            ControlTypeName = "Window",
            Name = "W",
            Children = { new FakeNode { ControlTypeName = "Button", Name = "A" } },
        };
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            CreateWalker().Walk("w1", tree, RefFactory(out _), cts.Token));
    }

    private class FakeNode : IUiNode
    {
        public string? Name { get; init; }

        public string? AutomationId { get; init; }

        public string ControlTypeName { get; init; } = "Pane";

        public bool IsEnabled { get; init; } = true;

        public bool IsOffscreen { get; init; }

        public bool? IsReadOnly { get; init; }

        public string? ToggleState { get; init; }

        public bool? IsSelected { get; init; }

        public string? ExpandCollapseState { get; init; }

        public List<IUiNode> Children { get; init; } = [];

        public virtual IReadOnlyList<IUiNode> GetChildren() => Children;
    }

    private sealed class SlowChildrenNode : FakeNode
    {
        public override IReadOnlyList<IUiNode> GetChildren()
        {
            Thread.Sleep(20);
            return base.GetChildren();
        }
    }

    private sealed class HostileNode : IUiNode
    {
        public string? Name => throw new InvalidOperationException("name unavailable");

        public string? AutomationId => "hostile";

        public string ControlTypeName => throw new InvalidOperationException("role unavailable");

        public bool IsEnabled => throw new InvalidOperationException("state unavailable");

        public bool IsOffscreen => throw new InvalidOperationException("state unavailable");

        public bool? IsReadOnly => throw new InvalidOperationException("state unavailable");

        public string? ToggleState => throw new InvalidOperationException("state unavailable");

        public bool? IsSelected => throw new InvalidOperationException("state unavailable");

        public string? ExpandCollapseState => throw new InvalidOperationException("state unavailable");

        public IReadOnlyList<IUiNode> GetChildren() => [];
    }
}
