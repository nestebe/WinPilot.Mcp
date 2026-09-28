namespace WinPilot.Automation.Capture;

/// <summary>Parameters for <see cref="IWindowsAutomation.CaptureAsync"/>.</summary>
/// <param name="Handle">Window handle to capture; null captures the foreground window.</param>
/// <param name="Ref">Element reference to capture; overrides <paramref name="Handle"/>.</param>
/// <param name="FullScreen">Captures the entire screen instead of a window or element.</param>
/// <param name="Background">Uses native background capture; requires a window handle only.</param>
/// <param name="SavePath">Optional absolute local <c>.png</c> path to save the image to.</param>
/// <param name="Overwrite">Allows <paramref name="SavePath"/> to replace an existing file.</param>
public sealed record CaptureRequest(
    string? Handle,
    string? Ref,
    bool FullScreen,
    bool Background,
    string? SavePath,
    bool Overwrite);
