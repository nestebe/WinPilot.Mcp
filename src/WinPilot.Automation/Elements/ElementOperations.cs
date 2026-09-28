using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using WinPilot.Automation.Errors;
using WinPilot.Automation.Input;

namespace WinPilot.Automation.Elements;

/// <summary>
/// Low-level element interactions (pattern-first click, typing, filling, key sending, text reads,
/// selector lookup) executed on the UI Automation worker thread.
/// </summary>
internal static class ElementOperations
{
    /// <summary>Returns whether the element can be interacted with (enabled and on-screen).</summary>
    public static bool IsUsable(AutomationElement element)
    {
        try
        {
            return element.Properties.IsEnabled.ValueOrDefault && !element.Properties.IsOffscreen.ValueOrDefault;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>Finds the first descendant matching the selector; null when nothing matches.</summary>
    public static AutomationElement? FindBySelector(Window window, ElementSelector selector)
    {
        ArgumentNullException.ThrowIfNull(window);
        selector.Validate();

        var controlType = ResolveControlType(selector.ControlType);

        if (!string.IsNullOrEmpty(selector.AutomationId))
        {
            var byId = window.FindFirstDescendant(cf => cf.ByAutomationId(selector.AutomationId));
            if (byId is not null && MatchesControlType(byId, controlType))
            {
                return byId;
            }
        }

        if (!string.IsNullOrEmpty(selector.Name))
        {
            var byName = window.FindFirstDescendant(cf => cf.ByName(selector.Name));
            if (byName is not null && MatchesControlType(byName, controlType))
            {
                return byName;
            }
        }

        if (!string.IsNullOrEmpty(selector.Name) && controlType is not null)
        {
            return window.FindFirstDescendant(cf => cf.ByName(selector.Name).And(cf.ByControlType(controlType.Value)));
        }

        return null;
    }

    /// <summary>Pattern-first click: Invoke, Toggle, SelectionItem, then a real mouse click.</summary>
    public static string Click(AutomationElement element, string button, bool doubleClick, string refId)
    {
        var name = SafeName(element) ?? refId;
        var leftSingle = string.Equals(button, "left", StringComparison.OrdinalIgnoreCase) && !doubleClick;

        if (leftSingle && element.Patterns.Invoke.IsSupported)
        {
            element.Patterns.Invoke.Pattern.Invoke();
            return $"Invoked {name}";
        }

        if (leftSingle && element.Patterns.Toggle.IsSupported)
        {
            element.Patterns.Toggle.Pattern.Toggle();
            var state = element.Patterns.Toggle.Pattern.ToggleState.ValueOrDefault;
            return $"Toggled {name} to {state}";
        }

        if (leftSingle && element.Patterns.SelectionItem.IsSupported)
        {
            element.Patterns.SelectionItem.Pattern.Select();
            return $"Selected {name}";
        }

        var point = element.GetClickablePoint();
        var mouseButton = button.ToLowerInvariant() switch
        {
            "right" => MouseButton.Right,
            "middle" => MouseButton.Middle,
            _ => MouseButton.Left,
        };

        if (doubleClick)
        {
            Mouse.DoubleClick(point, mouseButton);
            return $"Double-clicked {name}";
        }

        Mouse.Click(point, mouseButton);
        return $"Clicked {name}";
    }

    /// <summary>Types text into the focused element after focusing <paramref name="element"/> when given.</summary>
    public static string Type(AutomationElement? element, string text, bool submit, string? refId)
    {
        if (element is not null)
        {
            element.Focus();
            Thread.Sleep(50);
        }

        Keyboard.Type(text);

        if (submit)
        {
            Keyboard.Press(VirtualKeyShort.ENTER);
        }

        var action = submit ? "Typed and submitted" : "Typed";
        var target = element is null ? "the focused element" : SafeName(element) ?? refId ?? "element";
        return $"{action} \"{text}\" into {target}";
    }

    /// <summary>Clears and fills the element: Value pattern when writable, focus + select-all + type otherwise.</summary>
    public static string Fill(AutomationElement element, string value, string refId)
    {
        var name = SafeName(element) ?? refId;

        if (element.Patterns.Value.IsSupported && !element.Patterns.Value.Pattern.IsReadOnly.ValueOrDefault)
        {
            element.Patterns.Value.Pattern.SetValue(value);
            return $"Filled {name} with \"{value}\"";
        }

        element.Focus();
        Thread.Sleep(50);
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
        Thread.Sleep(50);
        Keyboard.Type(value);
        return $"Filled {name} with \"{value}\"";
    }

    /// <summary>Sends parsed key chords to the element (focused first) or the focused element.</summary>
    public static string SendKeys(AutomationElement? element, IReadOnlyList<KeyChord> chords, string? refId)
    {
        if (element is not null)
        {
            element.Focus();
            Thread.Sleep(50);
        }

        foreach (var chord in chords)
        {
            if (chord.Modifiers.Count == 0)
            {
                Keyboard.Press(chord.Key);
            }
            else
            {
                Keyboard.TypeSimultaneously([.. chord.Modifiers, chord.Key]);
            }
        }

        var target = element is null ? "the focused element" : SafeName(element) ?? refId ?? "element";
        var described = string.Join(", ", chords.Select(DescribeChord));
        return $"Sent {described} to {target}";
    }

    /// <summary>Reads text using the same precedence as the original server.</summary>
    public static string GetText(AutomationElement element)
    {
        string? text = null;

        if (element.Patterns.Value.IsSupported)
        {
            text = element.Patterns.Value.Pattern.Value.ValueOrDefault;
        }

        if (string.IsNullOrEmpty(text) && element.Patterns.Selection.IsSupported)
        {
            var selected = element.Patterns.Selection.Pattern.Selection.ValueOrDefault;
            if (selected is { Length: > 0 })
            {
                text = selected[0].Properties.Name.ValueOrDefault;
            }
        }

        if (string.IsNullOrEmpty(text) && element.Patterns.LegacyIAccessible.IsSupported)
        {
            text = element.Patterns.LegacyIAccessible.Pattern.Value.ValueOrDefault;
        }

        if (string.IsNullOrEmpty(text))
        {
            text = element.Properties.Name.ValueOrDefault;
        }

        if (string.IsNullOrEmpty(text) && element.Patterns.Text.IsSupported)
        {
            text = element.Patterns.Text.Pattern.DocumentRange.GetText(-1);
        }

        return text ?? string.Empty;
    }

    /// <summary>Formats a one-line snapshot-style description of a live element.</summary>
    public static ElementInfo Describe(string refId, AutomationElement element)
    {
        var name = SafeName(element);
        var role = SafeRole(element);
        var line = name is null ? $"{role} [ref={refId}]" : $"{role} \"{name}\" [ref={refId}]";
        return new ElementInfo(refId, line);
    }

    private static string? SafeName(AutomationElement element)
    {
        try
        {
            var name = element.Properties.Name.ValueOrDefault;
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return null;
        }
    }

    private static string SafeRole(AutomationElement element)
    {
        try
        {
            return element.Properties.ControlType.ValueOrDefault switch
            {
                ControlType.Button => "button",
                ControlType.Edit => "textbox",
                ControlType.Text => "text",
                ControlType.CheckBox => "checkbox",
                ControlType.RadioButton => "radio",
                ControlType.ComboBox => "combobox",
                ControlType.ListItem => "listitem",
                ControlType.MenuItem => "menuitem",
                ControlType.TabItem => "tab",
                ControlType.TreeItem => "treeitem",
                ControlType.Hyperlink => "link",
                ControlType.Slider => "slider",
                ControlType.Window => "window",
                ControlType.Group => "group",
                ControlType.Pane => "group",
                _ => "element",
            };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return "element";
        }
    }

    private static bool MatchesControlType(AutomationElement element, ControlType? controlType)
    {
        if (controlType is null)
        {
            return true;
        }

        try
        {
            return element.Properties.ControlType.ValueOrDefault == controlType.Value;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return false;
        }
    }

    private static ControlType? ResolveControlType(string? controlType)
    {
        if (string.IsNullOrEmpty(controlType))
        {
            return null;
        }

        if (Enum.TryParse<ControlType>(controlType, ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        throw new InvalidArgumentException(
            $"Unknown controlType '{controlType}'.",
            "Examples: Button, Edit, Text, ListItem, MenuItem, TabItem, CheckBox, ComboBox, Window, Group, Pane.");
    }

    private static string DescribeChord(KeyChord chord)
    {
        var modifiers = chord.Modifiers.Select(m => m.ToString());
        return string.Join("+", [.. modifiers, chord.Key.ToString()]);
    }
}
