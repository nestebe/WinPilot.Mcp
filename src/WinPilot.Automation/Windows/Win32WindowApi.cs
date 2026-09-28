using System.Runtime.InteropServices;

namespace WinPilot.Automation.Windows;

/// <summary>A raw top-level window as reported by <c>EnumWindows</c>.</summary>
internal readonly record struct RawWindow(IntPtr Hwnd, string Title, int ProcessId);

/// <summary>
/// Native window-level operations. These do not use UI Automation, so they never block on a
/// wedged UIA provider and stay available while the UI Automation worker is busy.
/// </summary>
internal static class Win32WindowApi
{
    private const int GwlExStyle = -20;
    private const long WsExToolWindow = 0x00000080L;
    private const int DwmwaCloaked = 14;
    private const uint WmClose = 0x0010;
    private const int SwRestore = 9;

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    /// <summary>Enumerates visible, titled, non-cloaked top-level windows.</summary>
    public static IReadOnlyList<RawWindow> EnumerateTopLevelWindows()
    {
        var results = new List<RawWindow>();

        _ = EnumWindows((hwnd, lParam) =>
        {
            if (!IsWindowVisible(hwnd))
            {
                return true;
            }

            var exStyle = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
            if ((exStyle & WsExToolWindow) != 0)
            {
                return true;
            }

            if (IsCloaked(hwnd))
            {
                return true;
            }

            var title = ReadTitle(hwnd);
            if (string.IsNullOrWhiteSpace(title))
            {
                return true;
            }

            _ = GetWindowThreadProcessId(hwnd, out var processId);
            results.Add(new RawWindow(hwnd, title, (int)processId));
            return true;
        }, IntPtr.Zero);

        return results;
    }

    /// <summary>Returns whether the native window still exists.</summary>
    public static bool IsWindowAlive(IntPtr hwnd) => IsWindow(hwnd);

    /// <summary>Posts a graceful close message to the window.</summary>
    public static bool TryClose(IntPtr hwnd) => PostMessage(hwnd, WmClose, IntPtr.Zero, IntPtr.Zero);

    /// <summary>Restores and brings the window to the foreground.</summary>
    public static bool TryFocus(IntPtr hwnd)
    {
        _ = ShowWindow(hwnd, SwRestore);
        return SetForegroundWindow(hwnd);
    }

    /// <summary>Resolves a process name from a process id; null when unavailable.</summary>
    public static string? GetProcessName(int processId)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(processId);
            return process.ProcessName;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return null;
        }
    }

    private static string ReadTitle(IntPtr hwnd)
    {
        var length = GetWindowTextLength(hwnd);
        if (length <= 0)
        {
            return string.Empty;
        }

        var buffer = new char[length + 2];
        var copied = GetWindowText(hwnd, buffer, buffer.Length);
        return copied > 0 ? new string(buffer, 0, copied) : string.Empty;
    }

    private static bool IsCloaked(IntPtr hwnd)
    {
        try
        {
            return DwmGetWindowAttribute(hwnd, DwmwaCloaked, out var cloaked, sizeof(int)) == 0 && cloaked != 0;
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            return false; // On systems without DWM, cloaked windows do not exist.
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowTextLength(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowText(IntPtr hwnd, [Out] char[] text, int maxCount);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hwnd, int command);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
}
