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
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint TokenQuery = 0x0008;
    private const int TokenElevationClass = 20;

    private static readonly Lazy<bool> IsSelfElevated = new(DetectSelfElevation);

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

    /// <summary>Returns the current foreground window handle.</summary>
    public static IntPtr GetForegroundWindow() => GetForegroundWindowNative();

    /// <summary>
    /// Returns whether Windows blocks UI Automation interaction with the process (for example an
    /// elevated app viewed from a non-elevated server): UIPI silently returns an empty tree.
    /// </summary>
    public static bool IsInteractionBlocked(int processId)
    {
        var process = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (process == IntPtr.Zero)
        {
            return true; // protected process or different user: not automatable from here
        }

        try
        {
            if (!OpenProcessToken(process, TokenQuery, out var token))
            {
                return true; // access denied (typically: the target is elevated and we are not)
            }

            try
            {
                if (!GetTokenInformation(token, TokenElevationClass, out var elevated, sizeof(int), out _))
                {
                    return true;
                }

                return elevated != 0 && !IsSelfElevated.Value;
            }
            finally
            {
                _ = CloseHandle(token);
            }
        }
        finally
        {
            _ = CloseHandle(process);
        }
    }

    private static bool DetectSelfElevation()
    {
        if (!OpenProcessToken(GetCurrentProcess(), TokenQuery, out var token))
        {
            return false;
        }

        try
        {
            return GetTokenInformation(token, TokenElevationClass, out var elevated, sizeof(int), out _) && elevated != 0;
        }
        finally
        {
            _ = CloseHandle(token);
        }
    }

    /// <summary>Posts a graceful close message to the window.</summary>
    public static bool TryClose(IntPtr hwnd) => PostMessage(hwnd, WmClose, IntPtr.Zero, IntPtr.Zero);

    /// <summary>Restores and brings the window to the foreground.</summary>
    public static bool TryFocus(IntPtr hwnd)
    {
        _ = ShowWindow(hwnd, SwRestore);
        _ = BringWindowToTop(hwnd);

        if (SetForegroundWindow(hwnd))
        {
            return true;
        }

        // Windows refuses foreground changes from background processes ("foreground lock").
        // Attaching to the current foreground thread lifts the restriction for the call.
        var foreground = GetForegroundWindowNative();
        if (foreground == IntPtr.Zero)
        {
            return false;
        }

        var foregroundThread = GetWindowThreadProcessId(foreground, out _);
        var currentThread = GetCurrentThreadId();
        if (foregroundThread == currentThread || !AttachThreadInput(currentThread, foregroundThread, true))
        {
            return false;
        }

        try
        {
            return SetForegroundWindow(hwnd);
        }
        finally
        {
            _ = AttachThreadInput(currentThread, foregroundThread, false);
        }
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

    [DllImport("user32.dll", EntryPoint = "GetForegroundWindow", SetLastError = true)]
    private static extern IntPtr GetForegroundWindowNative();

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
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BringWindowToTop(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, [MarshalAs(UnmanagedType.Bool)] bool attach);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(IntPtr token, int informationClass, out int information, int informationLength, out int returnLength);
}
