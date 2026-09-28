using FlaUI.Core.WindowsAPI;
using WinPilot.Automation.Errors;

namespace WinPilot.Automation.Input;

/// <summary>
/// Parses agent-facing key input — either one chord (<c>Ctrl+Shift+S</c>) or a sequence of chords
/// (<c>["Ctrl+C", "Down", "Enter"]</c>) — into <see cref="KeyChord"/> values. Pure and unit-testable.
/// </summary>
internal static class SendKeysParser
{
    /// <summary>
    /// Parses exactly one of <paramref name="chord"/> or <paramref name="keys"/>.
    /// </summary>
    /// <exception cref="InvalidArgumentException">Both or neither were provided, or a token is unknown.</exception>
    public static IReadOnlyList<KeyChord> Parse(string? chord, IReadOnlyList<string>? keys)
    {
        var hasChord = !string.IsNullOrWhiteSpace(chord);
        var hasKeys = keys is { Count: > 0 };

        if (hasChord == hasKeys)
        {
            throw new InvalidArgumentException(
                "Provide exactly one of 'chord' or 'keys'.",
                "Example: chord 'Ctrl+Right', or keys ['Ctrl+C', 'Down'].");
        }

        return hasChord
            ? [ParseChord(chord!)]
            : [.. keys!.Select(ParseChord)];
    }

    private static KeyChord ParseChord(string chord)
    {
        var tokens = chord.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            throw new InvalidArgumentException(
                $"Invalid key chord: '{chord}'.",
                "Example chords: 'Ctrl+A', 'Alt+F4', 'Enter'.");
        }

        var modifiers = new List<VirtualKeyShort>();
        for (var i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i];
            var isLast = i == tokens.Length - 1;

            if (KeyMap.IsModifier(token))
            {
                if (isLast)
                {
                    throw new InvalidArgumentException(
                        $"Chord '{chord}' must end with a non-modifier key.",
                        "Example: 'Ctrl+S'.");
                }

                modifiers.Add(KeyMap.Resolve(token)!.Value);
                continue;
            }

            if (!isLast)
            {
                throw new InvalidArgumentException(
                    $"Token '{token}' in chord '{chord}' is not a modifier.",
                    "Modifiers are: ctrl, shift, alt, win. Example: 'Ctrl+Shift+S'.");
            }

            var key = KeyMap.Resolve(token) ?? throw new InvalidArgumentException(
                $"Unknown key '{token}' in chord '{chord}'.",
                KeyMap.SupportedTokensHint);

            return new KeyChord(modifiers, key);
        }

        throw new InvalidArgumentException($"Invalid key chord: '{chord}'.", KeyMap.SupportedTokensHint);
    }
}
