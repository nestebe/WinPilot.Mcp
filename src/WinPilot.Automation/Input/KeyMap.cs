using FlaUI.Core.WindowsAPI;

namespace WinPilot.Automation.Input;

/// <summary>
/// Token-to-key mapping for <see cref="SendKeysParser"/>. Mirrors the token set of the original
/// FlaUI-MCP server so existing agent prompts keep working.
/// </summary>
internal static class KeyMap
{
    private static readonly Dictionary<string, VirtualKeyShort> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ctrl"] = VirtualKeyShort.CONTROL,
        ["control"] = VirtualKeyShort.CONTROL,
        ["shift"] = VirtualKeyShort.SHIFT,
        ["alt"] = VirtualKeyShort.ALT,
        ["menu"] = VirtualKeyShort.ALT,
        ["win"] = VirtualKeyShort.LWIN,
        ["windows"] = VirtualKeyShort.LWIN,
        ["meta"] = VirtualKeyShort.LWIN,
        ["enter"] = VirtualKeyShort.ENTER,
        ["return"] = VirtualKeyShort.ENTER,
        ["tab"] = VirtualKeyShort.TAB,
        ["space"] = VirtualKeyShort.SPACE,
        ["esc"] = VirtualKeyShort.ESCAPE,
        ["escape"] = VirtualKeyShort.ESCAPE,
        ["left"] = VirtualKeyShort.LEFT,
        ["right"] = VirtualKeyShort.RIGHT,
        ["up"] = VirtualKeyShort.UP,
        ["down"] = VirtualKeyShort.DOWN,
        ["home"] = VirtualKeyShort.HOME,
        ["end"] = VirtualKeyShort.END,
        ["pageup"] = VirtualKeyShort.PRIOR,
        ["pagedown"] = VirtualKeyShort.NEXT,
        ["backspace"] = VirtualKeyShort.BACK,
        ["bksp"] = VirtualKeyShort.BACK,
        ["delete"] = VirtualKeyShort.DELETE,
        ["del"] = VirtualKeyShort.DELETE,
        ["insert"] = VirtualKeyShort.INSERT,
        ["ins"] = VirtualKeyShort.INSERT,
        ["printscreen"] = VirtualKeyShort.SNAPSHOT,
        ["prtsc"] = VirtualKeyShort.SNAPSHOT,
        ["prtscr"] = VirtualKeyShort.SNAPSHOT,
        ["pause"] = VirtualKeyShort.PAUSE,
        ["break"] = VirtualKeyShort.PAUSE,
        ["media_next"] = VirtualKeyShort.MEDIA_NEXT_TRACK,
        ["media_prev"] = VirtualKeyShort.MEDIA_PREV_TRACK,
        ["media_play_pause"] = VirtualKeyShort.MEDIA_PLAY_PAUSE,
        ["media_stop"] = VirtualKeyShort.MEDIA_STOP,
        ["f1"] = VirtualKeyShort.F1,
        ["f2"] = VirtualKeyShort.F2,
        ["f3"] = VirtualKeyShort.F3,
        ["f4"] = VirtualKeyShort.F4,
        ["f5"] = VirtualKeyShort.F5,
        ["f6"] = VirtualKeyShort.F6,
        ["f7"] = VirtualKeyShort.F7,
        ["f8"] = VirtualKeyShort.F8,
        ["f9"] = VirtualKeyShort.F9,
        ["f10"] = VirtualKeyShort.F10,
        ["f11"] = VirtualKeyShort.F11,
        ["f12"] = VirtualKeyShort.F12,
        ["a"] = VirtualKeyShort.KEY_A,
        ["b"] = VirtualKeyShort.KEY_B,
        ["c"] = VirtualKeyShort.KEY_C,
        ["d"] = VirtualKeyShort.KEY_D,
        ["e"] = VirtualKeyShort.KEY_E,
        ["f"] = VirtualKeyShort.KEY_F,
        ["g"] = VirtualKeyShort.KEY_G,
        ["h"] = VirtualKeyShort.KEY_H,
        ["i"] = VirtualKeyShort.KEY_I,
        ["j"] = VirtualKeyShort.KEY_J,
        ["k"] = VirtualKeyShort.KEY_K,
        ["l"] = VirtualKeyShort.KEY_L,
        ["m"] = VirtualKeyShort.KEY_M,
        ["n"] = VirtualKeyShort.KEY_N,
        ["o"] = VirtualKeyShort.KEY_O,
        ["p"] = VirtualKeyShort.KEY_P,
        ["q"] = VirtualKeyShort.KEY_Q,
        ["r"] = VirtualKeyShort.KEY_R,
        ["s"] = VirtualKeyShort.KEY_S,
        ["t"] = VirtualKeyShort.KEY_T,
        ["u"] = VirtualKeyShort.KEY_U,
        ["v"] = VirtualKeyShort.KEY_V,
        ["w"] = VirtualKeyShort.KEY_W,
        ["x"] = VirtualKeyShort.KEY_X,
        ["y"] = VirtualKeyShort.KEY_Y,
        ["z"] = VirtualKeyShort.KEY_Z,
        ["0"] = VirtualKeyShort.KEY_0,
        ["1"] = VirtualKeyShort.KEY_1,
        ["2"] = VirtualKeyShort.KEY_2,
        ["3"] = VirtualKeyShort.KEY_3,
        ["4"] = VirtualKeyShort.KEY_4,
        ["5"] = VirtualKeyShort.KEY_5,
        ["6"] = VirtualKeyShort.KEY_6,
        ["7"] = VirtualKeyShort.KEY_7,
        ["8"] = VirtualKeyShort.KEY_8,
        ["9"] = VirtualKeyShort.KEY_9,
    };

    private static readonly HashSet<string> ModifierTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "ctrl", "control", "shift", "alt", "menu", "win", "windows", "meta",
    };

    /// <summary>Gets the key for a token, or null when unknown.</summary>
    public static VirtualKeyShort? Resolve(string token) => Map.TryGetValue(token, out var key) ? key : null;

    /// <summary>Returns whether the token names a modifier key.</summary>
    public static bool IsModifier(string token) => ModifierTokens.Contains(token);

    /// <summary>A short, human-readable list of supported tokens for error hints.</summary>
    public static string SupportedTokensHint =>
        "Supported: ctrl, shift, alt, win, enter, tab, space, escape, arrows, home/end, pageup/pagedown, " +
        "backspace, delete, insert, printscreen, pause, f1-f12, a-z, 0-9, media_next, media_prev, media_play_pause, media_stop.";
}
