using WinPilot.Automation.Errors;

namespace WinPilot.Automation.Elements;

/// <summary>Selector used to find an element when no reference is available.</summary>
/// <param name="Name">Accessible name to match exactly.</param>
/// <param name="AutomationId">Automation id to match exactly.</param>
/// <param name="ControlType">Control type name (for example <c>Button</c>).</param>
public sealed record ElementSelector(string? Name, string? AutomationId, string? ControlType)
{
    /// <summary>Returns whether no field is set.</summary>
    public bool IsEmpty
        => string.IsNullOrEmpty(Name) && string.IsNullOrEmpty(AutomationId) && string.IsNullOrEmpty(ControlType);

    /// <summary>Validates that at least one field is set.</summary>
    /// <exception cref="InvalidArgumentException">Every field is empty.</exception>
    public void Validate()
    {
        if (IsEmpty)
        {
            throw new InvalidArgumentException(
                "A selector needs at least one of 'name', 'automationId' or 'controlType'.",
                "Provide one field, or use a ref from windows_snapshot.");
        }
    }
}
