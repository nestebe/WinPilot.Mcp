namespace WinPilot.Automation.Errors;

/// <summary>
/// Base exception for all WinPilot engine failures. Carries a stable <see cref="WinPilotErrorCode"/>
/// and an optional actionable hint for agents.
/// </summary>
public class WinPilotException : Exception
{
    /// <summary>
    /// Initializes a new engine exception.
    /// </summary>
    /// <param name="code">Stable error code for this failure.</param>
    /// <param name="message">Human-readable failure description.</param>
    /// <param name="hint">Optional one-line actionable hint for agents.</param>
    /// <param name="inner">Optional inner exception.</param>
    public WinPilotException(WinPilotErrorCode code, string message, string? hint = null, Exception? inner = null)
        : base(message, inner)
    {
        Code = code;
        Hint = hint;
    }

    /// <summary>Gets the stable error code for this failure.</summary>
    public WinPilotErrorCode Code { get; }

    /// <summary>Gets an optional one-line actionable hint for agents.</summary>
    public string? Hint { get; }
}

/// <summary>Input arguments failed validation.</summary>
public sealed class InvalidArgumentException : WinPilotException
{
    /// <summary>Initializes the exception.</summary>
    public InvalidArgumentException(string message, string? hint = null, Exception? inner = null)
        : base(WinPilotErrorCode.InvalidArgument, message, hint, inner)
    {
    }
}

/// <summary>The window handle is unknown or dead.</summary>
public sealed class WindowNotFoundException : WinPilotException
{
    /// <summary>Initializes the exception.</summary>
    public WindowNotFoundException(string message, string? hint = null, Exception? inner = null)
        : base(WinPilotErrorCode.WindowNotFound, message, hint, inner)
    {
    }
}

/// <summary>The element reference is unknown.</summary>
public sealed class ElementNotFoundException : WinPilotException
{
    /// <summary>Initializes the exception.</summary>
    public ElementNotFoundException(string message, string? hint = null, Exception? inner = null)
        : base(WinPilotErrorCode.ElementNotFound, message, hint, inner)
    {
    }
}

/// <summary>The element reference can no longer be resolved; a new snapshot is required.</summary>
public sealed class ElementStaleException : WinPilotException
{
    /// <summary>Initializes the exception.</summary>
    public ElementStaleException(string message, string? hint = null, Exception? inner = null)
        : base(WinPilotErrorCode.ElementStale, message, hint, inner)
    {
    }
}

/// <summary>The operation exceeded the configured soft timeout.</summary>
public sealed class OperationTimeoutException : WinPilotException
{
    /// <summary>Initializes the exception.</summary>
    public OperationTimeoutException(string message, string? hint = null, Exception? inner = null)
        : base(WinPilotErrorCode.OperationTimeout, message, hint, inner)
    {
    }
}

/// <summary>The request waited behind a busy worker past the soft timeout.</summary>
public sealed class EngineBusyException : WinPilotException
{
    /// <summary>Initializes the exception.</summary>
    public EngineBusyException(string message, string? hint = null, Exception? inner = null)
        : base(WinPilotErrorCode.EngineBusy, message, hint, inner)
    {
    }
}

/// <summary>The engine is recycling its worker; the request can be retried shortly.</summary>
public sealed class EngineUnavailableException : WinPilotException
{
    /// <summary>Initializes the exception.</summary>
    public EngineUnavailableException(string message, string? hint = null, Exception? inner = null)
        : base(WinPilotErrorCode.EngineUnavailable, message, hint, inner)
    {
    }
}

/// <summary>Process launch or window discovery failed.</summary>
public sealed class LaunchFailedException : WinPilotException
{
    /// <summary>Initializes the exception.</summary>
    public LaunchFailedException(string message, string? hint = null, Exception? inner = null)
        : base(WinPilotErrorCode.LaunchFailed, message, hint, inner)
    {
    }
}

/// <summary>Screenshot capture failed.</summary>
public sealed class CaptureFailedException : WinPilotException
{
    /// <summary>Initializes the exception.</summary>
    public CaptureFailedException(string message, string? hint = null, Exception? inner = null)
        : base(WinPilotErrorCode.CaptureFailed, message, hint, inner)
    {
    }
}

/// <summary>The underlying UI Automation provider raised an error.</summary>
public sealed class UiProviderException : WinPilotException
{
    /// <summary>Initializes the exception.</summary>
    public UiProviderException(string message, string? hint = null, Exception? inner = null)
        : base(WinPilotErrorCode.UiProviderError, message, hint, inner)
    {
    }
}

/// <summary>The requested capability is not supported for this target.</summary>
public sealed class CapabilityNotSupportedException : WinPilotException
{
    /// <summary>Initializes the exception.</summary>
    public CapabilityNotSupportedException(string message, string? hint = null, Exception? inner = null)
        : base(WinPilotErrorCode.NotSupported, message, hint, inner)
    {
    }
}
