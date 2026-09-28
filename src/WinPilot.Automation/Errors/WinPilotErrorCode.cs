namespace WinPilot.Automation.Errors;

/// <summary>
/// Stable error codes exposed to MCP tool callers. Each value maps to a code prefix
/// in agent-facing error text (for example <c>WINDOW_NOT_FOUND</c>).
/// </summary>
public enum WinPilotErrorCode
{
    /// <summary>Input arguments failed validation before touching UI Automation.</summary>
    InvalidArgument,

    /// <summary>The window handle is unknown, dead, or no longer resolvable.</summary>
    WindowNotFound,

    /// <summary>The element reference is unknown.</summary>
    ElementNotFound,

    /// <summary>The element reference exists but can no longer be resolved; a new snapshot is required.</summary>
    ElementStale,

    /// <summary>The operation exceeded the configured soft timeout.</summary>
    OperationTimeout,

    /// <summary>The request waited behind a busy worker past the soft timeout.</summary>
    EngineBusy,

    /// <summary>The engine is recycling its worker; the request can be retried shortly.</summary>
    EngineUnavailable,

    /// <summary>Process launch or window discovery failed.</summary>
    LaunchFailed,

    /// <summary>Screenshot capture failed.</summary>
    CaptureFailed,

    /// <summary>The underlying UI Automation provider raised an error.</summary>
    UiProviderError,

    /// <summary>The requested capability is not supported for this element or target.</summary>
    NotSupported,
}
