using System.Reflection;
using System.Runtime.InteropServices;

namespace WinPilot.Mcp;

/// <summary>
/// Assembly marker used by tests to locate the server executable (and as a reflection anchor).
/// </summary>
public sealed class McpServerAssembly
{
    private McpServerAssembly()
    {
    }

    /// <summary>Gets the informational version of the server assembly (set by MinVer).</summary>
    public static string InformationalVersion
        => typeof(McpServerAssembly).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
           ?? "0.0.0";
}

/// <summary>Enables per-monitor DPI awareness so UI Automation coordinates match real pixels.</summary>
internal static class DpiInitialization
{
    private static readonly IntPtr PerMonitorV2 = new(-4);

    /// <summary>Best-effort per-monitor-v2 opt-in; ignored on systems that do not support it.</summary>
    public static void EnablePerMonitorV2()
    {
        try
        {
            _ = SetProcessDpiAwarenessContext(PerMonitorV2);
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            // Older Windows versions do not expose the API; DPI awareness then stays at the default.
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);
}
