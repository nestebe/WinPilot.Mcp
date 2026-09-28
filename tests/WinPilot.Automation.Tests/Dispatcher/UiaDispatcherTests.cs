using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WinPilot.Automation.Configuration;
using WinPilot.Automation.Dispatcher;
using WinPilot.Automation.Errors;

namespace WinPilot.Automation.Tests.Dispatcher;

public class UiaDispatcherTests
{
    private static readonly int[] ExpectedFifoOrder = [0, 1, 2];

    private sealed class FakeContext : IDisposable
    {
        public required int Id { get; init; }

        public int DisposeCount { get; private set; }

        public void Dispose() => DisposeCount++;
    }

    private sealed class ContextFactory
    {
        private int _count;

        public List<FakeContext> Created { get; } = [];

        public FakeContext Create()
        {
            var context = new FakeContext { Id = Interlocked.Increment(ref _count) };
            lock (Created)
            {
                Created.Add(context);
            }

            return context;
        }
    }

    private static UiaDispatcher<FakeContext> CreateDispatcher(ContextFactory factory, Action<WinPilotOptions>? configure = null)
    {
        var options = new WinPilotOptions();
        configure?.Invoke(options);
        return new UiaDispatcher<FakeContext>(
            factory.Create,
            Options.Create(options),
            NullLogger<UiaDispatcher<FakeContext>>.Instance);
    }

    [Fact]
    public async Task Runs_work_in_fifo_order_on_single_thread()
    {
        var factory = new ContextFactory();
        await using var dispatcher = CreateDispatcher(factory);
        var results = new List<(int ThreadId, int Index)>();
        var done = new TaskCompletionSource();

        for (var i = 0; i < 3; i++)
        {
            var index = i;
            _ = dispatcher.InvokeAsync(
                $"op-{index}",
                (_, _) =>
                {
                    lock (results)
                    {
                        results.Add((Environment.CurrentManagedThreadId, index));
                    }

                    if (index == 2)
                    {
                        done.TrySetResult();
                    }

                    return index;
                },
                CancellationToken.None);
        }

        await done.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        lock (results)
        {
            Assert.Equal(ExpectedFifoOrder, results.Select(r => r.Index).ToArray());
            Assert.Single(results.Select(r => r.ThreadId).Distinct());
        }
    }

    [Fact]
    public async Task Soft_timeout_returns_error_while_work_continues()
    {
        var factory = new ContextFactory();
        await using var dispatcher = CreateDispatcher(factory, o =>
        {
            o.OperationTimeoutSeconds = 1;
            o.HardTimeoutSeconds = 60;
        });
        using var gate = new ManualResetEventSlim(false);
        var workCompleted = false;

        var first = dispatcher.InvokeAsync(
            "slow-op",
            (_, token) =>
            {
                gate.Wait(TimeSpan.FromSeconds(10), token);
                workCompleted = true;
                return 1;
            },
            CancellationToken.None);

        var exception = await Assert.ThrowsAsync<OperationTimeoutException>(() => first);
        Assert.Contains("slow-op", exception.Message);

        gate.Set();
        await Task.Delay(200, TestContext.Current.CancellationToken);
        Assert.True(workCompleted);

        var second = await dispatcher.InvokeAsync("fast-op", (_, _) => 42, CancellationToken.None);
        Assert.Equal(42, second);
    }

    [Fact]
    public async Task Hard_timeout_recycles_worker_and_new_calls_succeed()
    {
        var factory = new ContextFactory();
        await using var dispatcher = CreateDispatcher(factory, o =>
        {
            o.OperationTimeoutSeconds = 1;
            o.HardTimeoutSeconds = 2;
        });
        using var gate = new ManualResetEventSlim(false);

        var wedged = dispatcher.InvokeAsync(
            "wedged",
            (_, token) =>
            {
                gate.Wait(TimeSpan.FromSeconds(30), token);
                return 1;
            },
            CancellationToken.None);

        await Assert.ThrowsAsync<OperationTimeoutException>(() => wedged);
        await Task.Delay(1300, TestContext.Current.CancellationToken); // move past the 2 s hard deadline since the wedged work started
        Assert.Equal(1, dispatcher.CurrentEpoch);

        var seenContextId = 0;
        var second = await dispatcher.InvokeAsync("healthy", (context, _) =>
        {
            seenContextId = context.Id;
            return 7;
        }, CancellationToken.None);

        Assert.Equal(7, second);
        Assert.Equal(2, dispatcher.CurrentEpoch);
        Assert.Equal(2, seenContextId);
        lock (factory.Created)
        {
            Assert.Equal(2, factory.Created.Count);
        }

        gate.Set();
        await WaitUntilAsync(() =>
        {
            lock (factory.Created)
            {
                return factory.Created[0].DisposeCount == 1;
            }
        }, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task New_requests_fail_fast_as_engine_busy_while_worker_is_past_soft_timeout()
    {
        var factory = new ContextFactory();
        await using var dispatcher = CreateDispatcher(factory, o =>
        {
            o.OperationTimeoutSeconds = 1;
            o.HardTimeoutSeconds = 60;
        });
        using var gate = new ManualResetEventSlim(false);

        var wedged = dispatcher.InvokeAsync(
            "wedged",
            (_, token) =>
            {
                gate.Wait(TimeSpan.FromSeconds(30), token);
                return 1;
            },
            CancellationToken.None);

        await Task.Delay(1400, TestContext.Current.CancellationToken); // the worker is now past the 1 s soft timeout

        var stopwatch = Stopwatch.StartNew();
        var exception = await Assert.ThrowsAsync<EngineBusyException>(() =>
            dispatcher.InvokeAsync("second", (_, _) => 2, CancellationToken.None));
        stopwatch.Stop();

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1), $"expected a fast failure, took {stopwatch.Elapsed}");
        Assert.Contains("wedged", exception.Message);

        gate.Set();
        await Assert.ThrowsAsync<OperationTimeoutException>(() => wedged);
    }

    [Fact]
    public async Task Cancellation_before_execution_throws_OperationCanceledException()
    {
        var factory = new ContextFactory();
        await using var dispatcher = CreateDispatcher(factory);
        var executed = false;
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            dispatcher.InvokeAsync(
                "cancelled",
                (_, _) =>
                {
                    executed = true;
                    return 1;
                },
                cts.Token));

        Assert.False(executed);
    }

    [Fact]
    public async Task Unknown_worker_exception_is_wrapped_as_UiProviderException()
    {
        var factory = new ContextFactory();
        await using var dispatcher = CreateDispatcher(factory);
        var providerError = new InvalidOperationException("provider died");

        var exception = await Assert.ThrowsAsync<UiProviderException>(() =>
            dispatcher.InvokeAsync<int>("boom", (_, _) => throw providerError, CancellationToken.None));

        Assert.Same(providerError, exception.InnerException);
    }

    [Fact]
    public async Task Dispose_stops_worker_and_disposes_context()
    {
        var factory = new ContextFactory();
        var dispatcher = CreateDispatcher(factory);

        await dispatcher.InvokeAsync("op", (_, _) => 1, CancellationToken.None);
        await dispatcher.DisposeAsync();

        Assert.Equal(1, factory.Created[0].DisposeCount);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(50, TestContext.Current.CancellationToken);
        }

        Assert.Fail($"Condition was not met within {timeout}.");
    }
}
