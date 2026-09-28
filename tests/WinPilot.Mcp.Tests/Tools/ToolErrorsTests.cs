using ModelContextProtocol.Protocol;
using WinPilot.Automation.Errors;
using WinPilot.Mcp.Tools;

namespace WinPilot.Mcp.Tests.Tools;

public class ToolErrorsTests
{
    [Fact]
    public void Wraps_engine_errors_with_code_and_hint()
    {
        var result = ToolErrors.ToErrorResult(new WindowNotFoundException(
            "Window not found: w9.",
            "Run windows_list_windows to see available windows."));

        Assert.True(result.IsError);
        var text = Assert.IsType<TextContentBlock>(result.Content[0]).Text;
        Assert.Equal(
            "WINDOW_NOT_FOUND: Window not found: w9. (Run windows_list_windows to see available windows.)",
            text);
    }

    [Fact]
    public void Omits_the_hint_parentheses_when_there_is_no_hint()
    {
        var result = ToolErrors.ToErrorResult(new EngineBusyException("The engine is busy."));

        var text = Assert.IsType<TextContentBlock>(result.Content[0]).Text;
        Assert.Equal("ENGINE_BUSY: The engine is busy.", text);
    }

    [Fact]
    public void Maps_unexpected_exceptions_to_a_provider_error()
    {
        var result = ToolErrors.ToErrorResult(new InvalidOperationException("boom"));

        Assert.True(result.IsError);
        var text = Assert.IsType<TextContentBlock>(result.Content[0]).Text;
        Assert.StartsWith("UI_PROVIDER_ERROR: boom", text, StringComparison.Ordinal);
        Assert.Contains("Retry", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Text_results_are_not_errors()
    {
        var result = ToolErrors.Text("hello");

        Assert.True(result.IsError is null or false);
        var text = Assert.IsType<TextContentBlock>(result.Content[0]).Text;
        Assert.Equal("hello", text);
    }

    [Theory]
    [InlineData(WinPilotErrorCode.InvalidArgument, "INVALID_ARGUMENT")]
    [InlineData(WinPilotErrorCode.UiProviderError, "UI_PROVIDER_ERROR")]
    [InlineData(WinPilotErrorCode.EngineUnavailable, "ENGINE_UNAVAILABLE")]
    [InlineData(WinPilotErrorCode.ElementStale, "ELEMENT_STALE")]
    public void Error_codes_are_screaming_snake_case(WinPilotErrorCode code, string expected)
        => Assert.Equal(expected, ToolErrors.CodeToText(code));
}
