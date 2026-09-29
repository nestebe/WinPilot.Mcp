using System.Runtime.InteropServices;
using Xunit;

namespace WinPilot.IntegrationTests;

/// <summary>Interactive-desktop detection for integration tests (dynamic skip in xunit v3).</summary>
internal static class TestEnvironment
{
    public static bool HasInteractiveDesktop { get; } = Detect();

    /// <summary>Gets the current foreground window handle.</summary>
    public static IntPtr ForegroundWindow => GetForegroundWindow();

    /// <summary>Skips the current test when no interactive Windows desktop session is available.</summary>
    public static void RequireInteractiveDesktop()
        => Assert.SkipWhen(!HasInteractiveDesktop, "Requires an interactive Windows desktop session.");

    private static bool Detect()
    {
        try
        {
            return Environment.UserInteractive && GetForegroundWindow() != IntPtr.Zero;
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
}
