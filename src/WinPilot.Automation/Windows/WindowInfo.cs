namespace WinPilot.Automation.Windows;

/// <summary>Describes one top-level window known to the current session.</summary>
/// <param name="Handle">Agent-facing window handle, for example <c>w1</c>.</param>
/// <param name="Hwnd">Native window handle.</param>
/// <param name="Title">Window title at last refresh.</param>
/// <param name="ProcessId">Owning process id.</param>
/// <param name="ProcessName">Owning process name when resolvable.</param>
public sealed record WindowInfo(string Handle, IntPtr Hwnd, string Title, int ProcessId, string? ProcessName);
