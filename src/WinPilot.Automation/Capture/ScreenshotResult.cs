namespace WinPilot.Automation.Capture;

/// <summary>A captured screenshot: PNG bytes and the path it was saved to, when requested.</summary>
/// <param name="Png">PNG-encoded image bytes.</param>
/// <param name="SavedPath">Absolute path the image was written to, or null.</param>
public sealed record ScreenshotResult(byte[] Png, string? SavedPath);
