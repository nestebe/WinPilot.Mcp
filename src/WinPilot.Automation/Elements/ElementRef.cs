using System.Globalization;

namespace WinPilot.Automation.Elements;

/// <summary>
/// Formats and parses agent-facing element references (<c>w1</c> windows, <c>w1e5</c> elements).
/// The format is a stable contract with MCP clients and must not change.
/// </summary>
internal static class ElementRef
{
    /// <summary>Formats a window handle, for example <c>w1</c>.</summary>
    public static string WindowHandle(int number)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(number, 1);
        return string.Create(CultureInfo.InvariantCulture, $"w{number}");
    }

    /// <summary>Formats an element reference within a window, for example <c>w1e5</c>.</summary>
    public static string For(string windowHandle, int index)
    {
        ArgumentException.ThrowIfNullOrEmpty(windowHandle);
        ArgumentOutOfRangeException.ThrowIfLessThan(index, 1);
        return string.Create(CultureInfo.InvariantCulture, $"{windowHandle}e{index}");
    }

    /// <summary>Parses an element reference into its window handle and index.</summary>
    public static bool TryParse(string refId, out string windowHandle, out int index)
    {
        windowHandle = string.Empty;
        index = 0;

        if (string.IsNullOrEmpty(refId) || refId[0] != 'w')
        {
            return false;
        }

        var separator = refId.IndexOf('e', 1);
        if (separator <= 1 || separator == refId.Length - 1)
        {
            return false;
        }

        if (!int.TryParse(refId.AsSpan(1, separator - 1), NumberStyles.None, CultureInfo.InvariantCulture, out var windowNumber)
            || windowNumber < 1)
        {
            return false;
        }

        if (!int.TryParse(refId.AsSpan(separator + 1), NumberStyles.None, CultureInfo.InvariantCulture, out index) || index < 1)
        {
            return false;
        }

        windowHandle = string.Create(CultureInfo.InvariantCulture, $"w{windowNumber}");
        return true;
    }
}
