using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using WinPilot.Automation.Elements;

namespace WinPilot.Automation.Dispatcher;

/// <summary>
/// One generation of UI Automation state: the automation client and the element registry.
/// Created by a context factory, owned and disposed by its worker thread.
/// </summary>
internal sealed class UiaContext : IDisposable
{
    /// <summary>Gets the UI Automation 3 client for this context generation.</summary>
    public UIA3Automation Automation { get; } = new();

    /// <summary>Gets the element registry confined to this context generation.</summary>
    public ElementRegistry<AutomationElement> Elements { get; } = new();

    /// <inheritdoc />
    public void Dispose() => Automation.Dispose();
}
