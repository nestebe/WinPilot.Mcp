using WinPilot.Automation.Capture;
using WinPilot.Automation.Elements;
using WinPilot.Automation.Windows;

namespace WinPilot.Automation;

/// <summary>
/// The WinPilot automation engine: window-level operations and UI Automation element operations.
/// All members are safe to call concurrently; UI Automation work is serialized internally.
/// </summary>
public interface IWindowsAutomation : IAsyncDisposable
{
    /// <summary>Launches an application and returns its window.</summary>
    /// <param name="app">Executable path or UWP app id.</param>
    /// <param name="args">Optional command line arguments.</param>
    /// <param name="timeoutMs">Optional window discovery timeout; defaults to the configured launch timeout.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    Task<WindowInfo> LaunchAsync(string app, IReadOnlyList<string>? args, int? timeoutMs, CancellationToken cancellationToken);

    /// <summary>Lists all top-level windows and refreshes the window registry.</summary>
    Task<IReadOnlyList<WindowInfo>> ListWindowsAsync(CancellationToken cancellationToken);

    /// <summary>Focuses a window by handle or by case-insensitive title substring.</summary>
    Task<WindowInfo> FocusWindowAsync(string? handle, string? title, CancellationToken cancellationToken);

    /// <summary>Closes a window; <paramref name="force"/> kills the process when the app is owned by this session.</summary>
    Task CloseWindowAsync(string handle, bool force, CancellationToken cancellationToken);

    /// <summary>Builds the accessibility snapshot of a window (or the foreground window when no handle is given).</summary>
    /// <param name="handle">Window handle, or null for the foreground window.</param>
    /// <param name="maxDepth">Optional snapshot depth override (clamped to 1..20).</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    Task<string> SnapshotAsync(string? handle, int? maxDepth, CancellationToken cancellationToken);

    /// <summary>Waits until an element (by ref or selector) exists, is enabled, and is on-screen.</summary>
    Task<ElementInfo> WaitForElementAsync(string? parentHandle, string? elementRef, ElementSelector? selector, int? timeoutMs, CancellationToken cancellationToken);

    /// <summary>Clicks an element: pattern-first (Invoke, Toggle, SelectionItem), mouse click as fallback.</summary>
    Task<string> ClickAsync(string elementRef, string button, bool doubleClick, CancellationToken cancellationToken);

    /// <summary>Types text into an element (focused first when a ref is given) or the focused element.</summary>
    Task<string> TypeAsync(string? elementRef, string text, bool submit, CancellationToken cancellationToken);

    /// <summary>Clears and fills a text element.</summary>
    Task<string> FillAsync(string elementRef, string value, CancellationToken cancellationToken);

    /// <summary>Sends one key chord or a sequence of chords to an element or the focused element.</summary>
    Task<string> SendKeysAsync(string? elementRef, string? chord, IReadOnlyList<string>? keys, CancellationToken cancellationToken);

    /// <summary>Reads the text content of an element.</summary>
    Task<string> GetTextAsync(string elementRef, CancellationToken cancellationToken);

    /// <summary>Captures a screenshot of the screen, a window, or an element.</summary>
    Task<ScreenshotResult> CaptureAsync(CaptureRequest request, CancellationToken cancellationToken);
}
