using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WinPilot.Automation.Configuration;
using WinPilot.Automation.Errors;

namespace WinPilot.Automation.Dispatcher;

/// <summary>
/// Executes all UI Automation work of one context type on a single dedicated worker thread.
/// </summary>
/// <remarks>
/// <para>
/// Wiring all UIA work through this dispatcher makes concurrent UI Automation access impossible by
/// construction: no call can ever run outside the worker thread. The MCP protocol layer therefore
/// never blocks on UI Automation and stays responsive to pings and cancellation notifications.
/// </para>
/// <para>
/// Timeouts are two-level: a soft timeout (for example 30 s) fails the caller while the in-flight
/// call keeps running and its eventual result is discarded; when a new request arrives while the
/// worker has been busy past the hard timeout (for example 90 s), the dispatcher recycles: it starts
/// a fresh worker thread with a fresh context and abandons the old one.
/// </para>
/// </remarks>
/// <typeparam name="TContext">Worker context type (production: UIA3Automation plus its registries).</typeparam>
[SuppressMessage(
    "Usage",
    "CA2213:Disposable fields should be disposed",
    Justification = "The context is disposed by its owning worker thread when the worker exits (correct COM thread affinity).")]
internal sealed class UiaDispatcher<TContext> : IAsyncDisposable
    where TContext : class, IDisposable
{
    private readonly Channel<WorkItem> _queue = Channel.CreateUnbounded<WorkItem>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    private readonly object _gate = new();
    private readonly Func<TContext> _contextFactory;
    private readonly WinPilotOptions _options;
    private readonly ILogger _logger;
    private readonly List<Thread> _threads = [];

    private CurrentItem? _currentItem;
    private TContext? _context;
    private int _epoch;
    private volatile bool _disposed;

    /// <summary>
    /// Initializes the dispatcher and starts the first worker thread.
    /// </summary>
    public UiaDispatcher(
        Func<TContext> contextFactory,
        IOptions<WinPilotOptions> options,
        ILogger<UiaDispatcher<TContext>> logger)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _contextFactory = contextFactory;
        _options = options.Value;
        _logger = logger;

        _context = contextFactory();
        _epoch = 1;
        StartWorker(_context, _epoch);
    }

    /// <summary>Gets the current context epoch; it increments on every worker recycle.</summary>
    public int CurrentEpoch
    {
        get
        {
            lock (_gate)
            {
                return _epoch;
            }
        }
    }

    /// <summary>
    /// Enqueues <paramref name="work"/> for serialized execution on the worker thread.
    /// </summary>
    /// <typeparam name="T">Result type.</typeparam>
    public async Task<T> InvokeAsync<T>(
        string operationName,
        Func<TContext, CancellationToken, T> work,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationName);
        ArgumentNullException.ThrowIfNull(work);
        cancellationToken.ThrowIfCancellationRequested();

        WorkItem item;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (IsCurrentItemOverdueLocked(_options.HardTimeoutSeconds))
            {
                RecycleLocked();
            }
            else if (IsCurrentItemOverdueLocked(_options.OperationTimeoutSeconds))
            {
                throw new EngineBusyException(
                    $"The engine is still running a long operation ('{_currentItem!.Value.OperationName}').",
                    "Wait for it to finish and retry. Window-level tools (windows_list_windows, windows_focus) do not use the busy worker.");
            }

            item = new WorkItem
            {
                OperationName = operationName,
                Work = (context, token) => work((TContext)context!, token),
                CancellationToken = cancellationToken,
                Completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously),
                EnqueuedTicks = Stopwatch.GetTimestamp(),
            };

            if (!_queue.Writer.TryWrite(item))
            {
                throw new EngineUnavailableException("The engine is shutting down.", "Restart the MCP server.");
            }
        }

        using var softCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var softDelay = Task.Delay(TimeSpan.FromSeconds(_options.OperationTimeoutSeconds), softCts.Token);
        if (await Task.WhenAny(item.Completion.Task, softDelay).ConfigureAwait(false) == softDelay)
        {
            item.Abandoned = true;
            cancellationToken.ThrowIfCancellationRequested();
            throw new OperationTimeoutException(
                $"Operation '{operationName}' timed out after {_options.OperationTimeoutSeconds}s.",
                "A modal dialog or a blocked UI Automation provider may still be busy; dismiss it and retry. Window-level tools remain available.");
        }

        softCts.Cancel();

        return (T)(await item.Completion.Task.ConfigureAwait(false))!;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        List<Thread> threads;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _queue.Writer.TryComplete();
            threads = [.. _threads];
        }

        await Task.Run(() =>
        {
            foreach (var thread in threads)
            {
                if (thread.IsAlive && !thread.Join(TimeSpan.FromSeconds(2)))
                {
                    _logger.LogWarning("Worker thread '{ThreadName}' did not stop within the shutdown grace period.", thread.Name);
                }
            }
        }).ConfigureAwait(false);
    }

    private static bool IsOverdue(long startedTicks, int seconds)
        => Stopwatch.GetElapsedTime(startedTicks).TotalSeconds > seconds;

    private static void StartThread(List<Thread> threads, Action body, string name)
    {
        var thread = new Thread(() => body())
        {
            IsBackground = true,
            Name = name,
        };
        threads.Add(thread);
        thread.Start();
    }

    private bool IsCurrentItemOverdueLocked(int seconds)
        => _currentItem is { } current && IsOverdue(current.StartedTicks, seconds);

    private void RecycleLocked()
    {
        var current = _currentItem!.Value;
        _logger.LogWarning(
            "Recycling the UI Automation worker: operation '{OperationName}' has been running for {ElapsedSeconds:0.0}s.",
            current.OperationName,
            Stopwatch.GetElapsedTime(current.StartedTicks).TotalSeconds);

        _currentItem = null;
        _epoch++;
        _context = _contextFactory();
        StartWorker(_context, _epoch);
    }

    private void StartWorker(TContext context, int workerEpoch)
        => StartThread(_threads, () => WorkerLoop(context, workerEpoch), $"WinPilot.UIA (epoch {workerEpoch})");

    private void WorkerLoop(TContext context, int workerEpoch)
    {
        try
        {
            while (!_disposed)
            {
                if (Volatile.Read(ref _epoch) != workerEpoch)
                {
                    return; // a newer worker took over after recycle
                }

                if (!_queue.Reader.TryRead(out var item))
                {
                    Thread.Sleep(1);
                    continue;
                }

                if (Volatile.Read(ref _epoch) != workerEpoch)
                {
                    item.Completion.TrySetException(new EngineUnavailableException(
                        $"The engine recycled its worker before '{item.OperationName}' started.",
                        "Retry the request."));
                    continue;
                }

                if (item.CancellationToken.IsCancellationRequested)
                {
                    item.Completion.TrySetCanceled(item.CancellationToken);
                    continue;
                }

                if (item.Abandoned)
                {
                    // The caller already gave up on this queued item; executing it now would
                    // apply side effects nobody is waiting for (for example a late click).
                    _logger.LogDebug("Skipped abandoned queued operation '{OperationName}'.", item.OperationName);
                    item.Completion.TrySetCanceled(item.CancellationToken);
                    continue;
                }

                ExecuteWorkItem(context, workerEpoch, item);
            }
        }
        finally
        {
            context.Dispose();
        }
    }

    private void ExecuteWorkItem(TContext context, int workerEpoch, WorkItem item)
    {
        lock (_gate)
        {
            _currentItem = new CurrentItem(workerEpoch, Stopwatch.GetTimestamp(), item.OperationName);
        }

        try
        {
            var result = item.Work(context, item.CancellationToken);
            if (item.Abandoned)
            {
                _logger.LogDebug("Discarded the result of abandoned operation '{OperationName}'.", item.OperationName);
            }
            else
            {
                item.Completion.TrySetResult(result);
            }
        }
        catch (OperationCanceledException)
        {
            if (!item.Abandoned)
            {
                item.Completion.TrySetCanceled(item.CancellationToken);
            }
        }
        catch (WinPilotException exception)
        {
            if (item.Abandoned)
            {
                _logger.LogDebug(exception, "Abandoned operation '{OperationName}' failed.", item.OperationName);
            }
            else
            {
                item.Completion.TrySetException(exception);
            }
        }
        catch (Exception exception)
        {
            var wrapped = new UiProviderException(
                $"UI Automation operation '{item.OperationName}' failed: {exception.Message}",
                "Retry the request; if it keeps failing, restart the target application.",
                exception);

            if (item.Abandoned)
            {
                _logger.LogDebug(wrapped, "Abandoned operation '{OperationName}' failed.", item.OperationName);
            }
            else
            {
                _logger.LogWarning(wrapped, "UI Automation operation '{OperationName}' failed.", item.OperationName);
                item.Completion.TrySetException(wrapped);
            }
        }
        finally
        {
            lock (_gate)
            {
                if (_currentItem is { } current && current.Epoch == workerEpoch)
                {
                    _currentItem = null;
                }
            }
        }
    }

    private readonly record struct CurrentItem(int Epoch, long StartedTicks, string OperationName);
}
