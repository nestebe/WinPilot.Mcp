using WinPilot.Automation;
using WinPilot.Automation.Capture;
using WinPilot.Automation.Elements;
using WinPilot.Automation.Errors;
using WinPilot.Automation.Windows;

namespace WinPilot.Mcp.Tests.Fakes;

/// <summary>Scriptable <see cref="IWindowsAutomation"/> for tool-layer tests.</summary>
internal sealed class FakeWindowsAutomation : IWindowsAutomation
{
    public List<string> Calls { get; } = [];

    public WindowInfo LaunchResult { get; set; } = new("w1", new IntPtr(1), "Test App", 42, "testapp");

    public IReadOnlyList<WindowInfo> Windows { get; set; } = [];

    public WindowInfo FocusResult { get; set; } = new("w1", new IntPtr(1), "Test App", 42, "testapp");

    public Func<string, IReadOnlyList<string>?, int?, CancellationToken, Task<WindowInfo>>? OnLaunch { get; set; }

    public Func<string, bool, CancellationToken, Task>? OnClose { get; set; }

    public Func<string, string, bool, CancellationToken, Task<string>>? OnClick { get; set; }

    public Func<string?, string, bool, CancellationToken, Task<string>>? OnType { get; set; }

    public Func<string, string, CancellationToken, Task<string>>? OnFill { get; set; }

    public Func<string?, string?, IReadOnlyList<string>?, CancellationToken, Task<string>>? OnSendKeys { get; set; }

    public Func<string, CancellationToken, Task<string>>? OnGetText { get; set; }

    public Func<string?, int?, CancellationToken, Task<string>>? OnSnapshot { get; set; }

    public Func<string?, string?, ElementSelector?, int?, CancellationToken, Task<ElementInfo>>? OnWaitFor { get; set; }

    public Func<CaptureRequest, CancellationToken, Task<ScreenshotResult>>? OnCapture { get; set; }

    public Task<WindowInfo> LaunchAsync(string app, IReadOnlyList<string>? args, int? timeoutMs, CancellationToken cancellationToken)
    {
        Calls.Add($"launch:{app}|{string.Join(',', args ?? [])}|{timeoutMs}");
        return OnLaunch is not null
            ? OnLaunch(app, args, timeoutMs, cancellationToken)
            : Task.FromResult(LaunchResult);
    }

    public Task<IReadOnlyList<WindowInfo>> ListWindowsAsync(CancellationToken cancellationToken)
    {
        Calls.Add("list");
        return Task.FromResult(Windows);
    }

    public Task<WindowInfo> FocusWindowAsync(string? handle, string? title, CancellationToken cancellationToken)
    {
        Calls.Add($"focus:{handle}|{title}");
        if (string.IsNullOrEmpty(handle) && string.IsNullOrEmpty(title))
        {
            throw new InvalidArgumentException("Either 'handle' or 'title' is required.");
        }

        return Task.FromResult(FocusResult);
    }

    public Task CloseWindowAsync(string handle, bool force, CancellationToken cancellationToken)
    {
        Calls.Add($"close:{handle}|{force}");
        return OnClose?.Invoke(handle, force, cancellationToken) ?? Task.CompletedTask;
    }

    public Task<string> ClickAsync(string elementRef, string button, bool doubleClick, CancellationToken cancellationToken)
    {
        Calls.Add($"click:{elementRef}|{button}|{doubleClick}");
        return OnClick is not null
            ? OnClick(elementRef, button, doubleClick, cancellationToken)
            : Task.FromResult($"Invoked {elementRef}");
    }

    public Task<string> TypeAsync(string? elementRef, string text, bool submit, CancellationToken cancellationToken)
    {
        Calls.Add($"type:{elementRef}|{text}|{submit}");
        return OnType is not null
            ? OnType(elementRef, text, submit, cancellationToken)
            : Task.FromResult($"Typed \"{text}\"");
    }

    public Task<string> FillAsync(string elementRef, string value, CancellationToken cancellationToken)
    {
        Calls.Add($"fill:{elementRef}|{value}");
        return OnFill is not null
            ? OnFill(elementRef, value, cancellationToken)
            : Task.FromResult($"Filled {elementRef} with \"{value}\"");
    }

    public Task<string> SendKeysAsync(string? elementRef, string? chord, IReadOnlyList<string>? keys, CancellationToken cancellationToken)
    {
        Calls.Add($"sendkeys:{elementRef}|{chord}|{string.Join(',', keys ?? [])}");
        return OnSendKeys is not null
            ? OnSendKeys(elementRef, chord, keys, cancellationToken)
            : Task.FromResult("Sent keys");
    }

    public Task<string> GetTextAsync(string elementRef, CancellationToken cancellationToken)
    {
        Calls.Add($"gettext:{elementRef}");
        return OnGetText is not null
            ? OnGetText(elementRef, cancellationToken)
            : Task.FromResult(string.Empty);
    }

    public Task<string> SnapshotAsync(string? handle, int? maxDepth, CancellationToken cancellationToken)
    {
        Calls.Add($"snapshot:{handle}|{maxDepth}");
        return OnSnapshot is not null
            ? OnSnapshot(handle, maxDepth, cancellationToken)
            : Task.FromResult("- window \"Test App\" [ref=w1]\n");
    }

    public Task<ElementInfo> WaitForElementAsync(string? parentHandle, string? elementRef, ElementSelector? selector, int? timeoutMs, CancellationToken cancellationToken)
    {
        Calls.Add($"wait:{parentHandle}|{elementRef}|{selector?.Name}|{timeoutMs}");
        return OnWaitFor is not null
            ? OnWaitFor(parentHandle, elementRef, selector, timeoutMs, cancellationToken)
            : Task.FromResult(new ElementInfo("w1e1", "button \"Save\" [ref=w1e1]"));
    }

    public Task<ScreenshotResult> CaptureAsync(CaptureRequest request, CancellationToken cancellationToken)
    {
        Calls.Add($"capture:{request.Handle}|{request.Ref}|{request.FullScreen}|{request.Background}|{request.SavePath}");
        return OnCapture is not null
            ? OnCapture(request, cancellationToken)
            : Task.FromResult(new ScreenshotResult([0x89, 0x50, 0x4E, 0x47], null));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
