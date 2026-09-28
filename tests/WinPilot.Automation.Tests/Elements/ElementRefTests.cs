using WinPilot.Automation.Elements;

namespace WinPilot.Automation.Tests.Elements;

public class ElementRefTests
{
    [Theory]
    [InlineData(1, "w1")]
    [InlineData(42, "w42")]
    public void Formats_window_handle(int number, string expected)
        => Assert.Equal(expected, ElementRef.WindowHandle(number));

    [Theory]
    [InlineData("w1e5", "w1", 5)]
    [InlineData("w12e340", "w12", 340)]
    public void Parses_element_ref(string refId, string window, int index)
    {
        Assert.True(ElementRef.TryParse(refId, out var parsedWindow, out var parsedIndex));
        Assert.Equal(window, parsedWindow);
        Assert.Equal(index, parsedIndex);
    }

    [Theory]
    [InlineData("x1e5")]
    [InlineData("w1")]
    [InlineData("w1eX")]
    [InlineData("")]
    [InlineData("w0e1")]
    [InlineData("w1e0")]
    public void Rejects_malformed_refs(string refId)
        => Assert.False(ElementRef.TryParse(refId, out _, out _));
}
