using System.Diagnostics;
using System.Runtime.InteropServices;
using FlaUI.Core.AutomationElements;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WinPilot.Automation.Capture;
using WinPilot.Automation.Configuration;
using WinPilot.Automation.Dispatcher;
using WinPilot.Automation.Elements;
using WinPilot.Automation.Errors;
using WinPilot.Automation.Input;
using WinPilot.Automation.Sessions;
using WinPilot.Automation.Snapshot;
using WinPilot.Automation.Windows;

namespace WinPilot.Automation;

/// <summary>
/// Default <see cref="IWindowsAutomation"/> implementation. Window-level operations use Win32 on the
/// caller thread; every UI Automation operation is serialized on a dedicated worker with soft/hard
/// timeouts and worker recycling.
/// </summary>
public sealed class WindowsAutomationEngine : IWindowsAutomation
{
    private const int MaxSnapshotDepth = 20;
    private const int StaleResolveTimeoutSeconds = 2;

    private readonly WinPilotOptions _options;
    private readonly SessionManager _session;
    private readonly UiaDispatcher<UiaContext> _dispatcher;

    // Worker-confined: written and read from UI Automation worker operations only.
    private readonly Dictionary<string, int> _snapshotEpochs = new(StringComparer.Ordinal);
    private int _disposed;

    /// <summary>Initializes the engine and starts its UI Automation worker.</summary>
    /// <param name="options">Validated engine options.</param>
    /// <param name="loggerFactory">Logger factory for engine-internal loggers.</param>
    public WindowsAutomationEngine(IOptions<WinPilotOptions> options, ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _options = options.Value;
        _options.Validate();

        _session = new SessionManager(_options, new LaunchService());
        _dispatcher = new UiaDispatcher<UiaContext>(
            static () => new UiaContext(),
            options,
            loggerFactory.CreateLogger<UiaDispatcher<UiaContext>>());
    }

    /// <summary>Clamps a requested snapshot depth to the supported range.</summary>
    internal static int ClampDepth(int? requested, int configured) => Math.Clamp(requested ?? configured, 1, MaxSnapshotDepth);

    /// <inheritdoc />
    public Task<WindowInfo> LaunchAsync(string app, IReadOnlyList<string>? args, int? timeoutMs, CancellationToken cancellationToken)
        => _session.LaunchAsync(app, args, timeoutMs, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<WindowInfo>> ListWindowsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_session.ListWindows());
    }

    /// <inheritdoc />
    public async Task<WindowInfo> FocusWindowAsync(string? handle, string? title, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var result = _session.Focus(handle, title);
        if (result.NativeFocusSucceeded)
        {
            return result.Window;
        }

        // SetForegroundWindow can be refused (foreground lock); fall back to UI Automation focus.
        return await _dispatcher.InvokeAsync("windows_focus", (context, _) =>
        {
            GetWindowOnWorker(context, result.Window).Focus();
            return result.Window;
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task CloseWindowAsync(string handle, bool force, CancellationToken cancellationToken)
        => _session.CloseAsync(handle, force, cancellationToken);

    /// <inheritdoc />
    public Task<string> SnapshotAsync(string? handle, int? maxDepth, CancellationToken cancellationToken)
        => _dispatcher.InvokeAsync("windows_snapshot", (context, token) =>
        {
            var epoch = _dispatcher.CurrentEpoch;
            var windowInfo = ResolveWindowInfo(handle);
            var window = GetWindowOnWorker(context, windowInfo);
            var depth = ClampDepth(maxDepth, _options.SnapshotMaxDepth);

            context.Elements.BeginSnapshot(windowInfo.Handle);
            var walker = new SnapshotWalker(new SnapshotWalkerOptions(
                depth,
                _options.SnapshotMaxNodes,
                _options.SnapshotTimeBudgetMs));
            var rootNode = new FlaUiNode(window);
            var node = walker.Walk(
                windowInfo.Handle,
                rootNode,
                candidate => ReferenceEquals(candidate, rootNode)
                    ? windowInfo.Handle
                    : RegisterNode(context, windowInfo.Handle, candidate),
                token);

            _snapshotEpochs[windowInfo.Handle] = epoch;
            return SnapshotFormatter.Format(node, depth);
        }, cancellationToken);

    /// <inheritdoc />
    public Task<ElementInfo> WaitForElementAsync(
        string? parentHandle,
        string? elementRef,
        ElementSelector? selector,
        int? timeoutMs,
        CancellationToken cancellationToken)
    {
        var hasRef = !string.IsNullOrWhiteSpace(elementRef);
        var hasSelector = selector is { IsEmpty: false };

        if (hasRef == hasSelector)
        {
            throw new InvalidArgumentException(
                "Provide exactly one of 'ref' or a selector (name/automationId/controlType).",
                "Example: ref 'w1e5', or handle 'w1' with name 'Save'.");
        }

        var timeout = timeoutMs ?? _options.WaitForElementTimeoutMs;
        if (timeout < 1)
        {
            throw new InvalidArgumentException(
                "timeoutMs must be greater than zero.",
                "Pass a positive timeout in milliseconds or omit it to use the configured default.");
        }

        return _dispatcher.InvokeAsync("windows_wait_for", (context, token) =>
        {
            var epoch = _dispatcher.CurrentEpoch;
            var windowInfo = ResolveWaitWindow(parentHandle, elementRef);
            var window = GetWindowOnWorker(context, windowInfo);

            var deadline = DateTime.UtcNow.AddMilliseconds(timeout);
            while (true)
            {
                token.ThrowIfCancellationRequested();

                var element = hasRef
                    ? ResolveElement(context, elementRef!, token)
                    : ElementOperations.FindBySelector(window, selector!);

                if (element is not null && ElementOperations.IsUsable(element))
                {
                    var refId = context.Elements.RegisterPreservingSnapshot(
                        windowInfo.Handle,
                        element,
                        ElementFingerprint.From(element));
                    _snapshotEpochs[windowInfo.Handle] = epoch;
                    return ElementOperations.Describe(refId, element);
                }

                if (DateTime.UtcNow >= deadline)
                {
                    throw new OperationTimeoutException(
                        $"No matching element appeared within {timeout} ms.",
                        "Increase timeoutMs, or run windows_snapshot to inspect the current tree.");
                }

                Thread.Sleep(100);
            }
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<string> ClickAsync(string elementRef, string button, bool doubleClick, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(elementRef);
        var normalizedButton = ValidateButton(button);

        return _dispatcher.InvokeAsync(
            "windows_click",
            (context, token) => ExecuteOnElement(
                context,
                elementRef,
                element => ElementOperations.Click(element, normalizedButton, doubleClick, elementRef),
                token),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<string> TypeAsync(string? elementRef, string text, bool submit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);

        return _dispatcher.InvokeAsync("windows_type", (context, token) =>
        {
            var element = string.IsNullOrEmpty(elementRef) ? null : ResolveElement(context, elementRef, token);
            return ElementOperations.Type(element, text, submit, elementRef);
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<string> FillAsync(string elementRef, string value, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(elementRef);
        ArgumentNullException.ThrowIfNull(value);

        return _dispatcher.InvokeAsync(
            "windows_fill",
            (context, token) => ExecuteOnElement(
                context,
                elementRef,
                element => ElementOperations.Fill(element, value, elementRef),
                token),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<string> SendKeysAsync(string? elementRef, string? chord, IReadOnlyList<string>? keys, CancellationToken cancellationToken)
    {
        var chords = SendKeysParser.Parse(chord, keys);

        return _dispatcher.InvokeAsync("windows_send_keys", (context, token) =>
        {
            var element = string.IsNullOrEmpty(elementRef) ? null : ResolveElement(context, elementRef, token);
            return ElementOperations.SendKeys(element, chords, elementRef);
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<string> GetTextAsync(string elementRef, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(elementRef);

        return _dispatcher.InvokeAsync(
            "windows_get_text",
            (context, token) => ExecuteOnElement(
                context,
                elementRef,
                ElementOperations.GetText,
                token),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<ScreenshotResult> CaptureAsync(CaptureRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Background && (request.FullScreen || !string.IsNullOrEmpty(request.Ref) || string.IsNullOrEmpty(request.Handle)))
        {
            throw new InvalidArgumentException(
                "background capture requires a window handle and cannot be combined with ref or fullScreen.",
                "Pass handle only, for example handle 'w1'.");
        }

        if (!SavePathValidator.TryNormalize(request.SavePath, request.Overwrite, out var normalizedPath, out var pathError))
        {
            throw new InvalidArgumentException(pathError, "Pass an absolute local .png path, or omit savePath.");
        }

        return _dispatcher.InvokeAsync("windows_screenshot", (context, token) =>
        {
            byte[] png;

            if (request.FullScreen)
            {
                png = CaptureService.CaptureScreen();
            }
            else if (!string.IsNullOrEmpty(request.Ref))
            {
                png = CaptureService.CaptureElement(ResolveElement(context, request.Ref, token));
            }
            else
            {
                var windowInfo = ResolveWindowInfo(request.Handle);
                png = request.Background && CaptureService.TryCaptureWindowBackground(windowInfo.Hwnd) is { } background
                    ? background
                    : CaptureService.CaptureElement(GetWindowOnWorker(context, windowInfo));
            }

            if (normalizedPath is not null)
            {
                CaptureService.SavePng(png, normalizedPath);
            }

            return new ScreenshotResult(png, normalizedPath);
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _dispatcher.DisposeAsync().ConfigureAwait(false);
        _session.Dispose();
        GC.SuppressFinalize(this);
    }

    private static string ValidateButton(string button)
    {
        var normalized = button?.ToLowerInvariant() ?? "left";
        return normalized switch
        {
            "left" or "right" or "middle" => normalized,
            _ => throw new InvalidArgumentException(
                $"Unknown mouse button '{button}'.",
                "Supported buttons: left, right, middle."),
        };
    }

    private static string RegisterNode(UiaContext context, string windowHandle, IUiNode node)
    {
        var element = ((FlaUiNode)node).Element;
        return context.Elements.Register(windowHandle, element, ElementFingerprint.From(element));
    }

    private static bool IsElementUnavailable(Exception exception)
        => exception is COMException { HResult: unchecked((int)0x80040201) } or InvalidComObjectException;

    private WindowInfo ResolveWindowInfo(string? handle)
        => string.IsNullOrEmpty(handle) ? ExpressForegroundWindow() : GetRegisteredWindow(handle);

    private WindowInfo GetRegisteredWindow(string handle)
        => _session.Windows.Get(handle)
           ?? throw new WindowNotFoundException($"Window not found: {handle}.", "Run windows_list_windows to see available windows.");

    private WindowInfo ExpressForegroundWindow()
    {
        var hwnd = Win32WindowApi.GetForegroundWindow();
        return _session.ListWindows().FirstOrDefault(window => window.Hwnd == hwnd)
               ?? throw new WindowNotFoundException(
                   "No foreground window detected.",
                   "Pass an explicit handle, or run windows_list_windows to see available windows.");
    }

    private WindowInfo ResolveWaitWindow(string? parentHandle, string? elementRef)
    {
        if (!string.IsNullOrEmpty(parentHandle))
        {
            return GetRegisteredWindow(parentHandle);
        }

        if (!string.IsNullOrEmpty(elementRef) && ElementRef.TryParse(elementRef, out var windowHandle, out _))
        {
            return GetRegisteredWindow(windowHandle);
        }

        return ExpressForegroundWindow();
    }

    private static Window GetWindowOnWorker(UiaContext context, WindowInfo info)
    {
        if (!Win32WindowApi.IsWindowAlive(info.Hwnd))
        {
            throw new WindowNotFoundException(
                $"Window {info.Handle} is no longer available.",
                "Run windows_list_windows to see available windows.");
        }

        if (Win32WindowApi.IsInteractionBlocked(info.ProcessId))
        {
            throw new UiProviderException(
                $"Window {info.Handle} belongs to '{info.ProcessName ?? "an application"}' running elevated (as administrator); Windows blocks UI Automation interaction from this non-elevated server.",
                "Run your MCP client (and this server) elevated to control administrator apps, or start the target app without administrator rights.");
        }

        try
        {
            return context.Automation.FromHandle(info.Hwnd).AsWindow();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new UiProviderException(
                $"Failed to attach to window {info.Handle}: {exception.Message}",
                "Retry the request; if it keeps failing, restart the target application.",
                exception);
        }
    }

    private AutomationElement ResolveElement(UiaContext context, string elementRef, CancellationToken cancellationToken)
    {
        if (!ElementRef.TryParse(elementRef, out var windowHandle, out _))
        {
            // Bare window handle (for example "w1"): the window element itself.
            return GetWindowOnWorker(context, GetRegisteredWindow(elementRef));
        }

        EnsureSnapshotEpoch(windowHandle);
        var lookup = context.Elements.Find(elementRef);

        switch (lookup.Status)
        {
            case ElementLookupStatus.Found:
                return lookup.Entry!.Element;

            case ElementLookupStatus.Stale:
                var windowInfo = GetRegisteredWindow(windowHandle);
                var window = GetWindowOnWorker(context, windowInfo);
                return ElementResolver.TryResolve(
                           window,
                           lookup.Entry!.Fingerprint,
                           TimeSpan.FromSeconds(StaleResolveTimeoutSeconds),
                           cancellationToken)
                       ?? throw new ElementStaleException(
                           $"Element ref '{elementRef}' is from an older snapshot and could not be re-resolved.",
                           "Run windows_snapshot and use the new refs.");

            default:
                throw new ElementNotFoundException(
                    $"Element not found: {elementRef}.",
                    "Run windows_snapshot to get valid refs.");
        }
    }

    private void EnsureSnapshotEpoch(string windowHandle)
    {
        if (_snapshotEpochs.TryGetValue(windowHandle, out var epoch) && epoch != _dispatcher.CurrentEpoch)
        {
            throw new ElementStaleException(
                $"Element refs for window {windowHandle} are from a previous engine session.",
                "Run windows_snapshot to refresh refs.");
        }
    }

    private T ExecuteOnElement<T>(
        UiaContext context,
        string elementRef,
        Func<AutomationElement, T> operation,
        CancellationToken cancellationToken)
    {
        var element = ResolveElement(context, elementRef, cancellationToken);

        try
        {
            return operation(element);
        }
        catch (Exception exception) when (IsElementUnavailable(exception) && ElementRef.TryParse(elementRef, out var windowHandle, out _))
        {
            var fingerprint = context.Elements.Find(elementRef).Entry?.Fingerprint;
            if (fingerprint is null)
            {
                throw;
            }

            var window = GetWindowOnWorker(context, GetRegisteredWindow(windowHandle));
            var retry = ElementResolver.TryResolve(window, fingerprint, TimeSpan.FromSeconds(1), cancellationToken)
                        ?? throw new ElementStaleException(
                            $"Element ref '{elementRef}' became unavailable.",
                            "Run windows_snapshot and use the new refs.");
            return operation(retry);
        }
    }
}
