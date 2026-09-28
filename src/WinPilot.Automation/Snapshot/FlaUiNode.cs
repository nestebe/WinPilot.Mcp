using FlaUI.Core.AutomationElements;

namespace WinPilot.Automation.Snapshot;

/// <summary>
/// Adapts a FlaUI <see cref="AutomationElement"/> to <see cref="IUiNode"/>. Thin mapping only;
/// defensive reads are the walker's responsibility.
/// </summary>
internal sealed class FlaUiNode : IUiNode
{
    /// <summary>Initializes a node wrapping a live element.</summary>
    public FlaUiNode(AutomationElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        Element = element;
    }

    /// <summary>Gets the wrapped element (used to register element references).</summary>
    public AutomationElement Element { get; }

    /// <inheritdoc />
    public string? Name => Element.Properties.Name.ValueOrDefault;

    /// <inheritdoc />
    public string? AutomationId => Element.Properties.AutomationId.ValueOrDefault;

    /// <inheritdoc />
    public string ControlTypeName => Element.Properties.ControlType.ValueOrDefault.ToString();

    /// <inheritdoc />
    public bool IsEnabled => Element.Properties.IsEnabled.ValueOrDefault;

    /// <inheritdoc />
    public bool IsOffscreen => Element.Properties.IsOffscreen.ValueOrDefault;

    /// <inheritdoc />
    public bool? IsReadOnly => Element.Patterns.Value.IsSupported
        ? Element.Patterns.Value.Pattern.IsReadOnly.ValueOrDefault
        : null;

    /// <inheritdoc />
    public string? ToggleState => Element.Patterns.Toggle.IsSupported
        ? Element.Patterns.Toggle.Pattern.ToggleState.ValueOrDefault.ToString()
        : null;

    /// <inheritdoc />
    public bool? IsSelected => Element.Patterns.SelectionItem.IsSupported
        ? Element.Patterns.SelectionItem.Pattern.IsSelected.ValueOrDefault
        : null;

    /// <inheritdoc />
    public string? ExpandCollapseState => Element.Patterns.ExpandCollapse.IsSupported
        ? Element.Patterns.ExpandCollapse.Pattern.ExpandCollapseState.ValueOrDefault.ToString()
        : null;

    /// <inheritdoc />
    public IReadOnlyList<IUiNode> GetChildren()
        => Element.FindAllChildren().Select(child => (IUiNode)new FlaUiNode(child)).ToList();
}
