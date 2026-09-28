using FlaUI.Core.WindowsAPI;
using WinPilot.Automation.Errors;
using WinPilot.Automation.Input;

namespace WinPilot.Automation.Tests.Input;

public class SendKeysParserTests
{
    [Fact]
    public void Parses_a_single_chord_with_modifiers()
    {
        var chords = SendKeysParser.Parse("Ctrl+Shift+S", null);

        var chord = Assert.Single(chords);
        Assert.Equal([VirtualKeyShort.CONTROL, VirtualKeyShort.SHIFT], chord.Modifiers);
        Assert.Equal(VirtualKeyShort.KEY_S, chord.Key);
    }

    [Fact]
    public void Parses_a_sequence_of_chords()
    {
        var chords = SendKeysParser.Parse(null, ["Ctrl+C", "Down", "Enter"]);

        Assert.Equal(3, chords.Count);
        Assert.Equal([VirtualKeyShort.CONTROL], chords[0].Modifiers);
        Assert.Equal(VirtualKeyShort.KEY_C, chords[0].Key);
        Assert.Empty(chords[1].Modifiers);
        Assert.Equal(VirtualKeyShort.DOWN, chords[1].Key);
        Assert.Equal(VirtualKeyShort.ENTER, chords[2].Key);
    }

    [Fact]
    public void Both_chord_and_keys_is_rejected()
        => Assert.Throws<InvalidArgumentException>(() => SendKeysParser.Parse("Ctrl+A", ["Enter"]));

    [Fact]
    public void Neither_chord_nor_keys_is_rejected()
        => Assert.Throws<InvalidArgumentException>(() => SendKeysParser.Parse(null, null));

    [Fact]
    public void Unknown_keys_are_rejected_with_a_supported_keys_hint()
    {
        var exception = Assert.Throws<InvalidArgumentException>(() => SendKeysParser.Parse("Ctrl+Foo", null));

        Assert.Contains("Foo", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Supported", exception.Hint!, StringComparison.Ordinal);
    }

    [Fact]
    public void A_chord_may_not_end_with_a_modifier()
    {
        Assert.Throws<InvalidArgumentException>(() => SendKeysParser.Parse("Ctrl", null));
        Assert.Throws<InvalidArgumentException>(() => SendKeysParser.Parse("Ctrl+Shift", null));
    }

    [Fact]
    public void Whitespace_around_tokens_is_tolerated()
    {
        var chords = SendKeysParser.Parse(" Ctrl + A ", null);

        var chord = Assert.Single(chords);
        Assert.Equal([VirtualKeyShort.CONTROL], chord.Modifiers);
        Assert.Equal(VirtualKeyShort.KEY_A, chord.Key);
    }

    [Theory]
    [InlineData("F5", VirtualKeyShort.F5)]
    [InlineData("Media_Next", VirtualKeyShort.MEDIA_NEXT_TRACK)]
    [InlineData("Escape", VirtualKeyShort.ESCAPE)]
    [InlineData("PageDown", VirtualKeyShort.NEXT)]
    [InlineData("PrintScreen", VirtualKeyShort.SNAPSHOT)]
    public void Known_aliases_map_to_keys(string token, VirtualKeyShort expected)
    {
        var chord = Assert.Single(SendKeysParser.Parse(token, null));
        Assert.Equal(expected, chord.Key);
    }

    [Fact]
    public void Empty_entries_in_keys_are_rejected()
        => Assert.Throws<InvalidArgumentException>(() => SendKeysParser.Parse(null, ["Ctrl+A", " "]));
}
