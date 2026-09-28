using System.Diagnostics;
using WinPilot.Automation.Elements;

namespace WinPilot.Automation.Snapshot;

/// <summary>
/// Walks a UI Automation tree into a <see cref="SnapshotNode"/> tree, skipping unnamed noise,
/// mapping control types to agent-friendly roles, and honoring depth/node/time budgets.
/// Runs on the UI Automation worker thread.
/// </summary>
internal sealed class SnapshotWalker
{
    private readonly SnapshotWalkerOptions _options;

    /// <summary>Initializes a walker with the given limits.</summary>
    public SnapshotWalker(SnapshotWalkerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <summary>Builds the snapshot tree for one window root.</summary>
    /// <param name="windowHandle">Window handle used for element references.</param>
    /// <param name="root">Root node (the window).</param>
    /// <param name="registerRef">Registers an emitted node and returns its agent-facing reference.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    public SnapshotNode Walk(
        string windowHandle,
        IUiNode root,
        Func<IUiNode, string> registerRef,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(windowHandle);
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(registerRef);

        var state = new WalkState(registerRef, _options, cancellationToken);
        return BuildNode(root, 0, state)
            ?? throw new InvalidOperationException("The snapshot root must not be skipped.");
    }

    private SnapshotNode? BuildNode(IUiNode node, int depth, WalkState state)
    {
        state.CancellationToken.ThrowIfCancellationRequested();

        var role = MapRole(TryRead(() => node.ControlTypeName, "Unknown"));
        var name = ReadName(node);
        if (ShouldSkip(name, role))
        {
            return null;
        }

        state.NodeCount++;
        var refId = state.RegisterRef(node);
        var states = ReadStates(node);

        var children = new List<SnapshotNode>();
        if (depth < _options.MaxDepth)
        {
            foreach (var child in TryReadChildren(node))
            {
                if (state.IsTimeBudgetExceeded)
                {
                    children.Add(SnapshotNode.Truncated(SnapshotTruncation.TimeBudget));
                    state.Stopped = true;
                    break;
                }

                if (state.NodeCount >= _options.MaxNodes)
                {
                    children.Add(SnapshotNode.Truncated(SnapshotTruncation.NodeLimit));
                    state.Stopped = true;
                    break;
                }

                var built = BuildNode(child, depth + 1, state);
                if (built is not null)
                {
                    children.Add(built);
                }

                if (state.Stopped)
                {
                    break;
                }
            }
        }
        else if (TryReadChildren(node).Count > 0)
        {
            children.Add(SnapshotNode.Truncated(SnapshotTruncation.DepthLimit));
        }

        return new SnapshotNode(role, name, refId, states, children);
    }

    private static string? ReadName(IUiNode node)
    {
        var name = TryRead(() => node.Name, null);
        if (!string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        var automationId = TryRead(() => node.AutomationId, null);
        return !string.IsNullOrWhiteSpace(automationId) && automationId.Length < 50 ? $"[{automationId}]" : null;
    }

    private static List<string> ReadStates(IUiNode node)
    {
        var states = new List<string>();

        if (!TryRead(() => node.IsEnabled, true))
        {
            states.Add("disabled");
        }

        if (TryRead(() => node.IsOffscreen, true))
        {
            states.Add("offscreen");
        }

        if (TryRead(() => node.IsReadOnly, false) == true)
        {
            states.Add("readonly");
        }

        var toggle = TryRead(() => node.ToggleState, null);
        if (string.Equals(toggle, "On", StringComparison.Ordinal))
        {
            states.Add("checked");
        }
        else if (string.Equals(toggle, "Indeterminate", StringComparison.Ordinal))
        {
            states.Add("indeterminate");
        }

        if (TryRead(() => node.IsSelected, false) == true)
        {
            states.Add("selected");
        }

        var expand = TryRead(() => node.ExpandCollapseState, null);
        if (string.Equals(expand, "Expanded", StringComparison.Ordinal))
        {
            states.Add("expanded");
        }
        else if (string.Equals(expand, "Collapsed", StringComparison.Ordinal))
        {
            states.Add("collapsed");
        }

        return states;
    }

    private static bool ShouldSkip(string? name, string role)
    {
        if (!string.IsNullOrEmpty(name))
        {
            return false;
        }

        return role switch
        {
            // Actionable elements stay even without a name.
            "button" or "textbox" or "checkbox" or "radio" or "combobox" or "listitem" or "menuitem"
                or "tab" or "treeitem" or "link" or "slider" => false,

            // Structural containers stay so their children can appear.
            "window" or "group" or "list" or "tree" or "tablist" or "menu" or "menubar" or "toolbar"
                or "grid" or "table" => false,

            // Decorative or structural elements without names are dropped.
            "element" or "thumb" or "scrollbar" or "separator" or "titlebar" => true,

            _ => false,
        };
    }

    private static string MapRole(string controlTypeName) => controlTypeName switch
    {
        "Button" => "button",
        "Edit" => "textbox",
        "Text" => "text",
        "CheckBox" => "checkbox",
        "RadioButton" => "radio",
        "ComboBox" => "combobox",
        "List" => "list",
        "ListItem" => "listitem",
        "Menu" => "menu",
        "MenuItem" => "menuitem",
        "MenuBar" => "menubar",
        "Tree" => "tree",
        "TreeItem" => "treeitem",
        "Tab" => "tablist",
        "TabItem" => "tab",
        "Table" => "table",
        "DataItem" => "row",
        "Header" => "header",
        "HeaderItem" => "columnheader",
        "Slider" => "slider",
        "Spinner" => "spinbutton",
        "ProgressBar" => "progressbar",
        "Hyperlink" => "link",
        "Image" => "image",
        "Pane" => "group",
        "Group" => "group",
        "Window" => "window",
        "Document" => "document",
        "ToolBar" => "toolbar",
        "ToolTip" => "tooltip",
        "ScrollBar" => "scrollbar",
        "StatusBar" => "status",
        "Separator" => "separator",
        "Thumb" => "thumb",
        "TitleBar" => "titlebar",
        "DataGrid" => "grid",
        "Custom" => "custom",
        _ => "element",
    };

    private static T TryRead<T>(Func<T> getter, T fallback)
    {
        try
        {
            return getter();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return fallback;
        }
    }

    private static IReadOnlyList<IUiNode> TryReadChildren(IUiNode node)
    {
        try
        {
            return node.GetChildren();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return [];
        }
    }

    private sealed class WalkState(
        Func<IUiNode, string> registerRef,
        SnapshotWalkerOptions options,
        CancellationToken cancellationToken)
    {
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

        public Func<IUiNode, string> RegisterRef { get; } = registerRef;

        public CancellationToken CancellationToken { get; } = cancellationToken;

        public int NodeCount { get; set; }

        public bool Stopped { get; set; }

        public bool IsTimeBudgetExceeded => _stopwatch.ElapsedMilliseconds > options.TimeBudgetMs;
    }
}
