namespace WinPilot.Automation.Dispatcher;

/// <summary>
/// A unit of work queued for the UI Automation worker thread.
/// </summary>
internal sealed class WorkItem
{
    /// <summary>Gets the operation name used in logs and timeout errors.</summary>
    public required string OperationName { get; init; }

    /// <summary>Gets the delegate executed on the worker thread; receives the current context.</summary>
    public required Func<object?, CancellationToken, object?> Work { get; init; }

    /// <summary>Gets the caller cancellation token (MCP cancellation).</summary>
    public required CancellationToken CancellationToken { get; init; }

    /// <summary>Gets the completion source the worker resolves.</summary>
    public required TaskCompletionSource<object?> Completion { get; init; }

    /// <summary>Gets the enqueue timestamp in <see cref="System.Diagnostics.Stopwatch"/> ticks.</summary>
    public required long EnqueuedTicks { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether the caller gave up (soft timeout).
    /// Results and errors of abandoned work items are logged and discarded.
    /// </summary>
    public volatile bool Abandoned;
}
