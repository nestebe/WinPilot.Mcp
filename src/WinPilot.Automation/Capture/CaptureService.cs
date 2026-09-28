using System.Drawing.Imaging;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using WinPilot.Automation.Errors;

namespace WinPilot.Automation.Capture;

/// <summary>
/// Screenshot capture: full screen, element, or window (with optional native background capture).
/// Capture calls run on the UI Automation worker thread; saving validates and writes PNG bytes.
/// </summary>
internal sealed class CaptureService
{
    /// <summary>Writes PNG bytes to an already-validated absolute path.</summary>
    public static void SavePng(byte[] png, string path)
    {
        ArgumentNullException.ThrowIfNull(png);
        ArgumentException.ThrowIfNullOrEmpty(path);
        File.WriteAllBytes(path, png);
    }

    /// <summary>Captures the entire virtual screen.</summary>
    public static byte[] CaptureScreen()
    {
        using var capture = FlaUI.Core.Capturing.Capture.Screen();
        return ToPng(capture);
    }

    /// <summary>Captures one element (window or control) from its screen bounds.</summary>
    public static byte[] CaptureElement(AutomationElement element)
    {
        using var capture = FlaUI.Core.Capturing.Capture.Element(element);
        return ToPng(capture);
    }

    /// <summary>
    /// Captures a window off-screen via <c>PrintWindow</c>; returns null when the native capture
    /// is unavailable or returns a blank frame (callers fall back to <see cref="CaptureElement"/>).
    /// </summary>
    public static byte[]? TryCaptureWindowBackground(IntPtr hwnd)
        => NativeWindowCapture.TryCaptureWindow(hwnd, out var png) ? png : null;

    private static byte[] ToPng(CaptureImage capture)
    {
        try
        {
            using var stream = new MemoryStream();
            capture.Bitmap.Save(stream, ImageFormat.Png);
            return stream.ToArray();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new CaptureFailedException(
                $"Screenshot capture failed: {exception.Message}",
                "Make sure the window is still open and visible on the desktop.",
                exception);
        }
    }
}
