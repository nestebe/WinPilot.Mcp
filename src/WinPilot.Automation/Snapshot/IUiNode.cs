namespace WinPilot.Automation.Snapshot;

/// <summary>
/// Read-only view of one UI Automation tree node. Production uses <see cref="FlaUiNode"/>;
/// unit tests use fakes. Property getters may throw (provider races); the walker reads defensively.
/// </summary>
internal interface IUiNode
{
    /// <summary>Gets the accessible name, when available.</summary>
    string? Name { get; }

    /// <summary>Gets the automation id, when available.</summary>
    string? AutomationId { get; }

    /// <summary>Gets the control type name (for example <c>Button</c>).</summary>
    string ControlTypeName { get; }

    /// <summary>Gets a value indicating whether the element is enabled.</summary>
    bool IsEnabled { get; }

    /// <summary>Gets a value indicating whether the element is off-screen.</summary>
    bool IsOffscreen { get; }

    /// <summary>Gets a value indicating whether the element is read-only (Value pattern), when available.</summary>
    bool? IsReadOnly { get; }

    /// <summary>Gets the toggle state name (<c>On</c>, <c>Off</c>, <c>Indeterminate</c>), when available.</summary>
    string? ToggleState { get; }

    /// <summary>Gets a value indicating whether the element is selected (SelectionItem pattern), when available.</summary>
    bool? IsSelected { get; }

    /// <summary>Gets the expand/collapse state name (<c>Expanded</c>, <c>Collapsed</c>, ...), when available.</summary>
    string? ExpandCollapseState { get; }

    /// <summary>Gets the children of this node.</summary>
    IReadOnlyList<IUiNode> GetChildren();
}
