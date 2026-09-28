using WinPilot.Automation.Elements;
using WinPilot.Automation.Errors;

namespace WinPilot.Automation.Tests.Elements;

public class ElementSelectorTests
{
    [Fact]
    public void An_empty_selector_is_rejected_with_a_hint()
    {
        var selector = new ElementSelector(null, null, null);

        var exception = Assert.Throws<InvalidArgumentException>(selector.Validate);
        Assert.Contains("selector", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(exception.Hint);
    }

    [Theory]
    [InlineData("Save", null, null)]
    [InlineData(null, "btnSave", null)]
    [InlineData(null, null, "Button")]
    [InlineData("Save", "btnSave", "Button")]
    public void Selectors_with_at_least_one_field_are_valid(string? name, string? automationId, string? controlType)
    {
        var selector = new ElementSelector(name, automationId, controlType);

        selector.Validate();
    }
}
