using FlaUI.Core.WindowsAPI;

namespace WinPilot.Automation.Input;

/// <summary>One key press: zero or more modifier keys followed by a key.</summary>
internal sealed record KeyChord(IReadOnlyList<VirtualKeyShort> Modifiers, VirtualKeyShort Key);
