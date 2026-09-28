using WinPilot.Automation.Errors;

namespace WinPilot.Automation.Tests.Errors;

public class WinPilotExceptionTests
{
    [Fact]
    public void Carries_code_and_hint()
    {
        var exception = new ElementStaleException(
            "Element ref 'w1e5' is from an older snapshot.",
            "Run windows_snapshot and use the new refs.");

        Assert.Equal(WinPilotErrorCode.ElementStale, exception.Code);
        Assert.Equal("Element ref 'w1e5' is from an older snapshot.", exception.Message);
        Assert.Equal("Run windows_snapshot and use the new refs.", exception.Hint);
    }

    [Fact]
    public void Inner_exception_is_preserved()
    {
        var inner = new InvalidOperationException("boom");

        var exception = new UiProviderException("The UI Automation provider failed.", "Retry the operation.", inner);

        Assert.Equal(WinPilotErrorCode.UiProviderError, exception.Code);
        Assert.Same(inner, exception.InnerException);
    }
}
