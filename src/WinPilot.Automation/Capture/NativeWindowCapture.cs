using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace WinPilot.Automation.Capture;

/// <summary>
/// Native background window capture via <c>PrintWindow</c> (PW_RENDERFULLCONTENT), with a blank
/// frame check so callers can fall back to a normal screen capture.
/// </summary>
internal static class NativeWindowCapture
{
    private const uint PwRenderFullContent = 0x00000002;

    /// <summary>Attempts a background capture; returns false when unavailable or blank.</summary>
    public static bool TryCaptureWindow(IntPtr hwnd, out byte[] png)
    {
        png = [];

        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var bounds))
        {
            return false;
        }

        var width = bounds.Right - bounds.Left;
        var height = bounds.Bottom - bounds.Top;
        if (width <= 0 || height <= 0)
        {
            return false;
        }

        try
        {
            using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                var hdc = graphics.GetHdc();
                try
                {
                    if (!PrintWindow(hwnd, hdc, PwRenderFullContent))
                    {
                        return false;
                    }
                }
                finally
                {
                    graphics.ReleaseHdc(hdc);
                }
            }

            if (IsBlank(bitmap))
            {
                return false;
            }

            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            png = stream.ToArray();
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return false;
        }
    }

    private static bool IsBlank(Bitmap bitmap)
    {
        var reference = bitmap.GetPixel(0, 0).ToArgb();
        var stepX = Math.Max(1, bitmap.Width / 16);
        var stepY = Math.Max(1, bitmap.Height / 16);

        for (var y = 0; y < bitmap.Height; y += stepY)
        {
            for (var x = 0; x < bitmap.Width; x += stepX)
            {
                if (bitmap.GetPixel(x, y).ToArgb() != reference)
                {
                    return false;
                }
            }
        }

        return true;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out Rect bounds);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
}
