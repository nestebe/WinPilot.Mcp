# WinPilot.Mcp Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rebuild FlaUI-MCP as WinPilot.Mcp — a well-architected, maintainable .NET 10 MCP server for Windows desktop automation that never disconnects.

**Architecture:** Two projects. `WinPilot.Automation` (class library): all UI Automation work serialized on one dedicated MTA worker thread (`UiaDispatcher`) with soft/hard timeouts and worker recycling; Win32 fast paths for window-level operations under lock-protected state; pure formatter/parser units for testability. `WinPilot.Mcp` (console exe): official MCP C# SDK stdio host with thin DI-injected tools; stdout carries only JSON-RPC; logs to stderr.

**Tech Stack:** .NET 10 (`net10.0-windows`), FlaUI.Core/UIA3 5.0.0, ModelContextProtocol 2.2.0, Microsoft.Extensions.Hosting/Options/Logging, MinVer, xunit v3.

**Spec:** `docs/superpowers/specs/2026-09-28-winpilot-mcp-design.md`

**Execution method:** Native (owner granted autonomous execution; one implementer session, TDD per task, per-task commits).

## Global Constraints

- Target framework `net10.0-windows`; SDK pinned by `global.json` to `10.0.401` (`rollForward: latestFeature`).
- `Nullable: enable`, `ImplicitUsings: enable`, `TreatWarningsAsErrors: true`, `AnalysisLevel: latest-recommended`, `EnforceCodeStyleInBuild: true`.
- Central Package Management via `Directory.Packages.props`; no version attributes on `PackageReference`.
- **stdout is protocol-only.** No `Console.Write*`/`Console.Out` anywhere in `src/`. Logging via `ILogger` (stderr console sink).
- Comments and identifiers in English. Public API XML docs on `WinPilot.Automation` public types.
- Ref format is `w{n}` / `w{n}e{k}` (agent-facing contract, unchanged from FlaUI-MCP).
- Snapshot text format unchanged: `- role "name" [ref=w1e5] [state]`, 2-space indent, role names and state names identical to the original.
- Defaults: operation timeout 30 s, hard timeout 90 s, snapshot depth 10 / nodes 2000 / budget 10 000 ms, launch window timeout 10 000 ms.
- All engine public async methods take `CancellationToken`; cancellation propagates as `OperationCanceledException`, never as error text.
- MIT license with attribution to FlaUI-MCP (Scott Hanselman) and FlaUI.

## Review Focus

The five input classes / failure modes most likely to bite, each pinned by a test in the owning task:

1. **A UIA call that never returns** (modal dialog, dead provider): soft timeout returns an actionable error, hard timeout recycles the worker, and window-level tools keep working during the wedge. Tests: Task 3, Task 17.
2. **Refs used after a new snapshot or after a recycle**: `ELEMENT_STALE` with re-snapshot hint, stale-ref one-shot re-resolution when the element can be re-located. Tests: Task 4, Task 9.
3. **Hostile element names / typed text** (quotes, backslashes, newlines, emoji, CJK): snapshot escaping is lossless-on-one-line; typing handles non-ASCII. Tests: Task 5, Task 17.
4. **Concurrent tool calls** from a client: serialized on the worker, both succeed, no interleaving corruption. Test: Task 18.
5. **Slow startup** (dotnet tool cold start, `dnx` first run) and stdout purity: server completes `initialize` promptly and never emits non-JSON on stdout. Tests: Task 12, Task 18.

---

### Task 1: Repository scaffold

**Files:**
- Create: `global.json`, `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig`, `WinPilot.Mcp.slnx`
- Create: `src/WinPilot.Automation/WinPilot.Automation.csproj`, `src/WinPilot.Automation/Placeholder.cs`
- Create: `src/WinPilot.Mcp/WinPilot.Mcp.csproj`, `src/WinPilot.Mcp/Program.cs` (temporary minimal)
- Create: `tests/WinPilot.Automation.Tests/WinPilot.Automation.Tests.csproj`
- Create: `tests/WinPilot.Mcp.Tests/WinPilot.Mcp.Tests.csproj`
- Create: `tests/WinPilot.IntegrationTests/WinPilot.IntegrationTests.csproj`

**Interfaces:**
- Consumes: nothing.
- Produces: buildable solution; `WinPilot.Automation` and `WinPilot.Mcp` namespaces; test project scaffolding used by every later task.

- [ ] **Step 1: Create root build files**

`global.json`:
```json
{ "sdk": { "version": "10.0.401", "rollForward": "latestFeature" } }
```

`Directory.Build.props`:
```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <AnalysisLevel>latest-recommended</AnalysisLevel>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <MinVerTagPrefix>v</MinVerTagPrefix>
    <MinVerMinimumMajorMinor>0.1</MinVerMinimumMajorMinor>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
</Project>
```

`Directory.Packages.props` (versions pinned here; `dotnet restore` will confirm):
```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <PackageVersion Include="FlaUI.Core" Version="5.0.0" />
    <PackageVersion Include="FlaUI.UIA3" Version="5.0.0" />
    <PackageVersion Include="ModelContextProtocol" Version="2.2.0" />
    <PackageVersion Include="Microsoft.Extensions.Hosting" Version="10.0.0" />
    <PackageVersion Include="Microsoft.Extensions.Options" Version="10.0.0" />
    <PackageVersion Include="MinVer" Version="6.0.0" />
    <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageVersion Include="xunit.v3" Version="3.0.1" />
    <PackageVersion Include="xunit.runner.visualstudio" Version="3.1.4" />
  </ItemGroup>
</Project>
```

`.editorconfig` (minimum viable):
```ini
root = true

[*]
charset = utf-8
end_of_line = crlf
insert_final_newline = true
trim_trailing_whitespace = true
indent_style = space
indent_size = 4
dotnet_sort_system_directives_first = true
csharp_style_namespace_declarations = file_scoped:warning

[*.{json,yml,yaml,csproj,props,targets}]
indent_size = 2
```

- [ ] **Step 2: Create projects**

`src/WinPilot.Automation/WinPilot.Automation.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <UseWindowsForms>true</UseWindowsForms>
    <RootNamespace>WinPilot.Automation</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="FlaUI.Core" />
    <PackageReference Include="FlaUI.UIA3" />
    <PackageReference Include="MinVer" PrivateAssets="all" />
  </ItemGroup>
</Project>
```

`src/WinPilot.Mcp/WinPilot.Mcp.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <UseWindowsForms>true</UseWindowsForms>
    <AssemblyName>winpilot-mcp</AssemblyName>
    <RootNamespace>WinPilot.Mcp</RootNamespace>
    <IsPackable>true</IsPackable>
    <PackAsTool>true</PackAsTool>
    <ToolCommandName>winpilot-mcp</ToolCommandName>
    <PackageId>WinPilot.Mcp</PackageId>
    <Description>MCP server for Windows desktop automation (UI Automation / FlaUI)</Description>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="ModelContextProtocol" />
    <PackageReference Include="Microsoft.Extensions.Hosting" />
    <PackageReference Include="MinVer" PrivateAssets="all" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\WinPilot.Automation\WinPilot.Automation.csproj" />
  </ItemGroup>
</Project>
```

Test projects (three identical shapes): `tests/<Name>/<Name>.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <UseWindowsForms>true</UseWindowsForms>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit.v3" />
    <PackageReference Include="xunit.runner.visualstudio" />
  </ItemGroup>
</Project>
```
Add `ProjectReference` to `WinPilot.Automation` in `WinPilot.Automation.Tests` and a reference to both `WinPilot.Automation` + `WinPilot.Mcp` (+ a later-added ProjectReference to the WinForms test app) in `WinPilot.Mcp.Tests` and `WinPilot.IntegrationTests`.

`Program.cs` temporary:
```csharp
Console.Error.WriteLine("WinPilot.Mcp scaffold");
```

`Placeholder.cs` in Automation: `namespace WinPilot.Automation; internal static class Placeholder;` (deleted in Task 2).

`slnx` (solution file; add all projects).
- [ ] **Step 3: Verify**

Run: `dotnet restore` then `dotnet build WinPilot.Mcp.slnx -warnaserror`
Expected: build succeeds.

> If MinVer 6.x or xunit.v3 versions differ, pick the latest stable at implementation time; the `PackageVersion` lines above are the only place to change.

- [ ] **Step 4: Commit**

```
git add -A
git commit -m "chore: scaffold WinPilot.Mcp solution"
```

---

### Task 2: Error taxonomy and options

**Files:**
- Create: `src/WinPilot.Automation/Errors/WinPilotErrorCode.cs`, `Errors/WinPilotException.cs`
- Create: `src/WinPilot.Automation/Configuration/WinPilotOptions.cs`
- Test: `tests/WinPilot.Automation.Tests/Errors/WinPilotExceptionTests.cs`, `tests/WinPilot.Automation.Tests/Configuration/WinPilotOptionsTests.cs`

**Interfaces:**
- Consumes: scaffold.
- Produces: `WinPilotErrorCode` (enum: `InvalidArgument, WindowNotFound, ElementNotFound, ElementStale, OperationTimeout, EngineBusy, EngineUnavailable, LaunchFailed, CaptureFailed, UiProviderError, NotSupported`); `WinPilotException(WinPilotErrorCode, string message, string? hint = null, Exception? inner = null)` with `Code`, `Hint`; sealed subclasses `InvalidArgumentException, WindowNotFoundException, ElementNotFoundException, ElementStaleException, OperationTimeoutException, EngineBusyException, EngineUnavailableException, LaunchFailedException, CaptureFailedException, UiProviderException`; `WinPilotOptions` (section name `WinPilot`, properties + defaults from Global Constraints, plus `CloseWindowTimeoutMs = 5000`, `WaitForElementTimeoutMs = 10000`, `KeepAppsOnExit = false`) and `void Validate()` throwing `InvalidArgumentException`/`ArgumentOutOfRangeException` on non-positive timeouts.

- [ ] **Step 1: Write failing tests**

```csharp
public class WinPilotOptionsTests
{
    [Fact]
    public void Defaults_match_spec() { var o = new WinPilotOptions();
        Assert.Equal(30, o.OperationTimeoutSeconds); Assert.Equal(90, o.HardTimeoutSeconds);
        Assert.Equal(2000, o.SnapshotMaxNodes); o.Validate(); }

    [Theory]
    [InlineData(0, 90, 10, 2000, 10000, 10000)]
    [InlineData(30, 0, 10, 2000, 10000, 10000)]
    public void Invalid_values_are_rejected(int op, int hard, int depth, int nodes, int budget, int launch)
    {
        var o = new WinPilotOptions { OperationTimeoutSeconds = op, HardTimeoutSeconds = hard,
            SnapshotMaxDepth = depth, SnapshotMaxNodes = nodes, SnapshotTimeBudgetMs = budget,
            LaunchWindowTimeoutMs = launch };
        Assert.Throws<ArgumentOutOfRangeException>(() => o.Validate());
    }
}

public class WinPilotExceptionTests
{
    [Fact]
    public void Carries_code_and_hint()
    {
        var ex = new ElementStaleException("Element not found: w1e5", "Run windows_snapshot to refresh refs.");
        Assert.Equal(WinPilotErrorCode.ElementStale, ex.Code);
        Assert.Contains("refresh", ex.Hint);
    }
}
```

- [ ] **Step 2: Run to fail** — `dotnet test tests/WinPilot.Automation.Tests` → FAIL (types missing).
- [ ] **Step 3: Implement** the enum, exception hierarchy (each subclass calls base with its code), and options with `Validate()` checking every millisecond/second value `> 0` and `HardTimeoutSeconds > OperationTimeoutSeconds` (else `ArgumentOutOfRangeException`).
- [ ] **Step 4: Run to pass** — same command → PASS.
- [ ] **Step 5: Commit** — `git commit -m "feat(automation): error taxonomy and options"`

---

### Task 3: UiaDispatcher — serialized worker, timeouts, recycle

**Files:**
- Create: `src/WinPilot.Automation/Dispatcher/UiaDispatcher.cs`, `Dispatcher/UiaContext.cs`, `Dispatcher/WorkItem.cs`
- Test: `tests/WinPilot.Automation.Tests/Dispatcher/UiaDispatcherTests.cs`

**Interfaces:**
- Consumes: `WinPilotOptions`, exceptions.
- Produces:
  - `UiaContext` — disposable holder created by a factory; production: `UIA3Automation` + `ElementRegistry` (Task 4); tests: fake with an `int Id` and a `DisposeCount`.
  - `UiaDispatcher(Func<UiaContext> contextFactory, IOptions<WinPilotOptions> options, ILogger<UiaDispatcher> logger)`; `int CurrentEpoch { get; }`; `Task<T> InvokeAsync<T>(string operationName, Func<UiaContext, CancellationToken, T> work, CancellationToken ct)`; `ValueTask DisposeAsync()`.
  - Behavior contract: FIFO; single execution thread; soft timeout → `OperationTimeoutException` to caller while work continues; hard timeout at enqueue-time of a new request → recycle (new context + new thread, epoch++); a queued item whose wait exceeded soft → `EngineBusyException`; worker exceptions pass through (`OperationCanceledException` when cancelled pre-execution, `WinPilotException` unchanged, others wrapped `UiProviderException`).

- [ ] **Step 1: Write failing tests**

```csharp
public class UiaDispatcherTests
{
    private static UiaDispatcher Create(TestClockPart? _, Action<WinPilotOptions>? configure = null) { /* builds options via IOptions from Configure */ }

    [Fact] public async Task Runs_work_in_fifo_order_on_single_thread()
    {
        // 3 quick items appending (threadId, index) to a channel; assert order and identical thread ids
    }

    [Fact] public async Task Soft_timeout_returns_error_while_work_continues()
    {
        // options: OperationTimeoutSeconds = 1; first item blocks on a ManualResetEventSlim gate.
        // InvokeAsync must throw OperationTimeoutException within ~2s; release gate; then second item succeeds.
    }

    [Fact] public async Task Hard_timeout_recycles_worker_and_new_calls_succeed()
    {
        // options: OperationTimeoutSeconds = 1; HardTimeoutSeconds = 2.
        // item1 blocks on gate1 (simulating a wedged provider).
        // await Task.Delay(2100) with the gate held; invoke item2 → must trigger recycle:
        // assert contextFactory call count == 2; item2 completes successfully while gate1 is still held;
        // assert item2 ran with the new context id; release gate1; assert old context eventually disposed.
    }

    [Fact] public async Task Queued_item_fails_fast_as_engine_busy_after_soft_deadline()
    {
        // item1 blocks >1s; item2 enqueued immediately; both use soft=1s.
        // item1 caller gets OperationTimeoutException; item2 caller gets EngineBusyException (never executed).
    }

    [Fact] public async Task Cancellation_before_execution_throws_OperationCanceledException()
    {
        // item1 blocks; item2 with an already-cancelled token → OperationCanceledException; work delegate never ran.
    }

    [Fact] public async Task Unknown_worker_exception_is_wrapped_as_UiProviderException()
    {
        // work throws COMException → caller sees UiProviderException with inner COMException.
    }
}
```

- [ ] **Step 2: Run to fail** — `dotnet test tests/WinPilot.Automation.Tests --filter UiaDispatcherTests` → FAIL.
- [ ] **Step 3: Implement**

Reference implementation shape (production code must match this contract):

```csharp
internal sealed class WorkItem
{
    public required string OperationName { get; init; }
    public required Func<UiaContext, CancellationToken, object?> Work { get; init; }
    public required CancellationToken CancellationToken { get; init; }
    public required TaskCompletionSource<object?> Completion { get; init; }
    public required long EnqueuedTicks { get; init; }
    public int EpochAtEnqueue { get; init; }
    public bool Abandoned { get; set; }
}

public sealed class UiaDispatcher : IAsyncDisposable
{
    private readonly Channel<WorkItem> _queue = Channel.CreateUnbounded<WorkItem>(
        new UnboundedChannelOptions { SingleReader = true });
    private readonly object _gate = new();
    private readonly Func<UiaContext> _contextFactory;
    private readonly WinPilotOptions _options;
    private readonly ILogger<UiaDispatcher> _logger;
    private UiaContext _context;
    private long _currentItemStartedTicks;   // Stopwatch.GetTimestamp() of the item being executed
    private int _epoch;
    private bool _disposed;

    public UiaDispatcher(Func<UiaContext> contextFactory, IOptions<WinPilotOptions> options, ILogger<UiaDispatcher> logger)
    {
        _contextFactory = contextFactory; _options = options.Value; _logger = logger;
        _context = contextFactory(); _epoch = _context.Epoch;
        StartWorkerThread(_context, _epoch);
    }

    public int CurrentEpoch => Volatile.Read(ref _epoch);

    public async Task<T> InvokeAsync<T>(string operationName, Func<UiaContext, CancellationToken, T> work, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var item = new WorkItem { OperationName = operationName, Work = (c, t) => work(c, t),
            CancellationToken = ct, Completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously),
            EnqueuedTicks = Stopwatch.GetTimestamp(), EpochAtEnqueue = CurrentEpoch };

        lock (_gate)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(UiaDispatcher));
            // Lazy hard-timeout recycle: a new request arriving while the current item has been
            // running longer than the hard deadline replaces the worker.
            if (_currentItemStartedTicks != 0 &&
                Elapsed(_currentItemStartedTicks) > _options.HardTimeoutSeconds)
            {
                RecycleLocked();
            }
            _queue.Writer.TryWrite(item);
        }

        using var softCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var softDelay = Task.Delay(TimeSpan.FromSeconds(_options.OperationTimeoutSeconds), softCts.Token);
        if (await Task.WhenAny(item.Completion.Task, softDelay) == softDelay)
        {
            item.Abandoned = true;
            ct.ThrowIfCancellationRequested();                       // client cancelled → OCE
            throw new OperationTimeoutException(
                $"Operation '{operationName}' timed out after {_options.OperationTimeoutSeconds}s.",
                "A modal dialog or a blocked UI Automation provider may still be busy; dismiss it and retry. Window-level tools (windows_list_windows, windows_focus) remain available.");
        }
        softCts.Cancel();
        return await Unwrap<T>(item.Completion.Task);
    }
    // RecycleLocked: _epoch++; _context = _contextFactory(); StartWorkerThread(_context, _epoch); _currentItemStartedTicks = 0;
    // StartWorkerThread: background MTA thread (no SetApartmentState — default MTA), loop:
    //   while (!disposed) { if (Volatile.Read(ref _epoch) != epoch) return;           // old worker exits
    //     if (!_queue.Reader.TryRead(out var item)) { Thread.Sleep(1); continue; }    // or WaitToReadAsync
    //     if (Volatile.Read(ref _epoch) != epoch) { item.Completion.TrySetException(new EngineUnavailableException(...)); continue; }
    //     if (item.CancellationToken.IsCancellationRequested) { item.Completion.TrySetCanceled(ct); continue; }
    //     if (Elapsed(item.EnqueuedTicks) > soft) { item.Completion.TrySetException(new EngineBusyException(...)); continue; }
    //     if (!item.Abandoned) _currentItemStartedTicks = Stopwatch.GetTimestamp();
    //     try   { var result = item.Work(_context, item.CancellationToken); item.Completion.TrySetResult(result); }
    //     catch (OperationCanceledException) { item.Completion.TrySetCanceled(); }
    //     catch (WinPilotException) { item.Completion.TrySetException(ex); }
    //     catch (Exception ex) when (!item.Abandoned) { item.Completion.TrySetException(new UiProviderException(...)); }
    //     catch (Exception) { /* abandoned: observe and log */ }
    //     finally { _currentItemStartedTicks = 0; }
    //     if (item.Abandoned) log warning.
    //   }
    // Unwrap<T>: await task; if task exception is TimeoutException/... cast T; wraps COMException (HRESULT) → UiProviderException.
    // DisposeAsync: complete the channel writer, join both threads (bounded 2s), dispose current context; abandoned contexts are disposed by their own worker when it exits? -> dispose left to GC finalizer/log; production disposes _context only.
}
```

Notes for the implementer: use `Stopwatch.GetTimestamp()`/`TimeSpan` helpers; never block the caller thread; `TryRead` + short `Thread.Sleep(1)` is acceptable for the worker's idle path (a dedicated thread, not a thread-pool thread); `TaskCreationOptions.RunContinuationsAsynchronously` is required to avoid deadlocks.

- [ ] **Step 4: Run to pass** — filter `UiaDispatcherTests` → PASS.
- [ ] **Step 5: Commit** — `git commit -m "feat(automation): serialized UIA dispatcher with soft/hard timeouts and recycling"`

---

### Task 4: Element refs, registry, fingerprint, stale re-resolution

**Files:**
- Create: `src/WinPilot.Automation/Elements/ElementRef.cs`, `Elements/ElementFingerprint.cs`, `Elements/ElementRegistry.cs`, `Elements/ElementResolver.cs`
- Test: `tests/WinPilot.Automation.Tests/Elements/ElementRefTests.cs`, `ElementResolutionTests.cs`

**Interfaces:**
- Consumes: dispatcher **only for production wiring**; registry itself is pure logic tested without FlaUI by using a `Fingerprint` record and an opaque `object` handle type parameterized as `AutomationElement` in production. For tests, use internal generic overloads: `ElementRegistry<TElement>`? — **Decision:** keep the registry non-generic over `AutomationElement` but isolate matching logic in a pure static `ElementMatcher` operating on `ElementFingerprint`, fully unit-testable.
- Produces:
  - `ElementRef` static helpers: `WindowHandle(int n) => "w{n}"`, `ElementRef.For(string window, int index)`, `bool TryParse(string, out string windowHandle, out int index)`.
  - `ElementFingerprint(string? AutomationId, string? Name, string ControlTypeName, string? RuntimeId)` with `static ElementFingerprint From(FlaUI AutomationElement)` (production only).
  - `ElementMatcher.FindMatch(ElementFingerprint target, IEnumerable<(string Ref, ElementFingerprint Fp)> candidates)` → exact `AutomationId` match wins; else `Name` + `ControlTypeName`; else `RuntimeId`; null.
  - `ElementRegistry` (worker-confined): `void BeginSnapshot(string windowHandle)` (clears that window's refs, increments `SnapshotVersion`), `string Register(string windowHandle, AutomationElement element)`, `RegisteredElement? Find(string elementRef)` (returns null when window version changed → caller throws `ElementStaleException`), `int SnapshotVersion(string windowHandle)`.
  - `ElementResolver` (worker): given a stale ref + window root element, re-resolve once via `ElementFingerprint` + FlaUI find conditions; `AutomationElement? TryResolve(Window window, ElementFingerprint fingerprint, TimeSpan timeout)`.

- [ ] **Step 1: Write failing tests** (pure; no FlaUI automation objects needed)

```csharp
public class ElementRefTests
{
    [Theory] [InlineData(1, "w1")] [InlineData(42, "w42")]
    public void Formats_window_handle(int n, string expected) => Assert.Equal(expected, ElementRef.WindowHandle(n));

    [Theory]
    [InlineData("w1e5", "w1", 5)]
    [InlineData("w12e340", "w12", 340)]
    public void Parses_element_ref(string refId, string window, int index)
    { Assert.True(ElementRef.TryParse(refId, out var w, out var i)); Assert.Equal(window, w); Assert.Equal(index, i); }

    [Theory] [InlineData("x1e5")] [InlineData("w1")] [InlineData("w1eX")] [InlineData("")]
    public void Rejects_malformed_refs(string refId) => Assert.False(ElementRef.TryParse(refId, out _, out _));
}

public class ElementResolutionTests
{
    [Fact] public void Prefers_automation_id_over_name_and_type()
    { /* candidates: same Name/Type on two refs, one AutomationId == target → that ref wins */ }

    [Fact] public void Falls_back_to_name_plus_control_type()
    { /* no AutomationId match → Name+ControlType match */ }

    [Fact] public void Returns_null_when_nothing_matches() { }

    [Fact] public void Reading_ref_after_new_snapshot_marks_it_stale()
    { /* registry: register, BeginSnapshot again → Find returns FindResult.Stale (or null + version mismatch flag) */ }
}
```

- [ ] **Step 2: Run to fail** — filter `ElementRefTests|ElementResolutionTests`.
- [ ] **Step 3: Implement.** Registry invariant: only touched on the worker; `BeginSnapshot` clears only that window's entries; refs remain `w{n}e{k}` with `k` reset per snapshot (parity). `Find` returns a result carrying `SnapshotVersion`; mismatch → `ElementStaleException("Element ref 'w1e5' is from an older snapshot.", "Run windows_snapshot and use the new refs.")`.
- [ ] **Step 4: Run to pass.** Filter the same.
- [ ] **Step 5: Commit** — `git commit -m "feat(automation): element refs, registry with snapshot versions, stale matching"`

---

### Task 5: Snapshot DTO and pure formatter

**Files:**
- Create: `src/WinPilot.Automation/Snapshot/SnapshotNode.cs`, `Snapshot/SnapshotFormatter.cs`
- Test: `tests/WinPilot.Automation.Tests/Snapshot/SnapshotFormatterTests.cs`

**Interfaces:**
- Consumes: nothing (pure).
- Produces: `SnapshotNode(string Role, string? Name, string Ref, IReadOnlyList<string> States, IReadOnlyList<SnapshotNode> Children, SnapshotTruncation? Truncation = null)` where `SnapshotTruncation` is `enum { DepthLimit, NodeLimit, TimeBudget }`; `SnapshotFormatter.Format(SnapshotNode root, int maxDepth)` → string, exact line format (see Global Constraints) and truncation lines `- ... (truncated: depth limit)`, `- ... (truncated: node limit reached)`, `- ... (truncated: time budget exceeded)` at their position in the tree; escaping of `\` → `\\`, `"` → `\"`, newline → `\n`, `\r` dropped; state rendering `[disabled]`, `[offscreen]`, `[readonly]`, `[checked]`, `[indeterminate]`, `[selected]`, `[expanded]`, `[collapsed]` in that fixed order.

- [ ] **Step 1: Write failing tests** (exact-string assertions)

```csharp
[Fact] public void Formats_exact_original_format()
{ var tree = Node("window","Calculator","w1", Child("button","Seven","w1e4", states:["disabled"]));
  Assert.Equal("- window \"Calculator\" [ref=w1]\n  - button \"Seven\" [ref=w1e4] [disabled]\n", SnapshotFormatter.Format(tree, 10)); }

[Fact] public void Escapes_quotes_backslashes_and_newlines()
{ /* name: `a"b\c\nd` → `"a\"b\\c\nd"` on ONE line */ }

[Fact] public void Omits_name_when_null()   // "- group [ref=w1e2]"
[Fact] public void Renders_states_in_fixed_order()  // ["disabled","checked"] regardless of input order? — formatter renders given order; walker emits fixed order. Assert combined case here.
[Fact] public void Depth_limit_marker_is_rendered_indented()  // node at depth==maxDepth with children still present → child line "- ... (truncated: depth limit)"
[Fact] public void Node_limit_and_time_budget_markers_render_in_place() { }
```

- [ ] **Step 2: Run to fail.**
- [ ] **Step 3: Implement** — `StringBuilder`, recursion with depth, `EscapeName`, state mapping constant order array.
- [ ] **Step 4: Run to pass.**
- [ ] **Step 5: Commit** — `git commit -m "feat(automation): snapshot DTO and pure formatter"`

---

### Task 6: Snapshot walker and FlaUI adapter seam

**Files:**
- Create: `src/WinPilot.Automation/Snapshot/IUiNode.cs`, `Snapshot/FlaUiNode.cs`, `Snapshot/SnapshotWalker.cs`, `Snapshot/SnapshotWalkerOptions.cs`
- Test: `tests/WinPilot.Automation.Tests/Snapshot/SnapshotWalkerTests.cs` (fake `IUiNode` tree)

**Interfaces:**
- Consumes: `SnapshotNode`, `SnapshotFormatter`, `ElementRegistry`, `ElementFingerprint`.
- Produces:
  - `IUiNode { string? Name; string? AutomationId; string ControlTypeName; bool IsEnabled; bool IsOffscreen; bool? IsReadOnly; string? ToggleState; bool? IsSelected; string? ExpandCollapseState; IReadOnlyList<IUiNode> GetChildren(); }`
  - `SnapshotWalker(SnapshotWalkerOptions options)` with `Task<SnapshotNode> WalkAsync(string windowHandle, IUiNode root, Func<IUiNode, string> registerRef, Func<IUiNode, IReadOnlyList<IUiNode>> childrenProvider, CancellationToken ct)`; skip rules identical to original (`ShouldSkip`: keep named elements, actionable roles, structural roles; drop unnamed `element`/`thumb`/`scrollbar`/`separator`/`titlebar`); role mapping table identical to the original; name fallback `[automationId]` when name empty; `MaxDepth`/`MaxNodes`/`TimeBudget` with `SnapshotTruncation` nodes; state order above; properties are read defensively (an `IUiNode` read may throw → treat as missing).
  - `FlaUiNode : IUiNode` (production adapter over `AutomationElement`, exposing `Element`); its property getters use FlaUI `ValueOrDefault` inside try/catch; `GetChildren` uses `FindAllChildren()`.

- [ ] **Step 1: Write failing tests** with a fake tree: skip behavior, fallback name `[automationId]`, node limit truncation (`MaxNodes = 3`), depth truncation, state extraction, defensive read (a property getter throws → element still emitted without that state).
- [ ] **Step 2: Run to fail.**
- [ ] **Step 3: Implement** walker (iterative or recursive with `Stopwatch` budget check at each node) + adapter.
- [ ] **Step 4: Run to pass.** Also run the whole Automation.Tests suite.
- [ ] **Step 5: Commit** — `git commit -m "feat(automation): snapshot walker with fake-tree seam and FlaUI adapter"`

---

### Task 7: Win32 window API and window registry

**Files:**
- Create: `src/WinPilot.Automation/Windows/Win32WindowApi.cs`, `Windows/WindowInfo.cs`, `Windows/WindowRegistry.cs`
- Test: `tests/WinPilot.Automation.Tests/Windows/WindowRegistryTests.cs`

**Interfaces:**
- Consumes: exceptions.
- Produces:
  - `WindowInfo(string Handle, IntPtr Hwnd, string Title, int ProcessId, string? ProcessName)`.
  - `Win32WindowApi` (static, internal): `IReadOnlyList<(IntPtr Hwnd, string Title, int Pid)> EnumerateTopLevelWindows()` (EnumWindows; skips invisible, `WS_EX_TOOLWINDOW`, DWM-cloaked), `bool IsWindowAlive(IntPtr)`, `bool TryClose(IntPtr hwnd)` (PostMessage WM_CLOSE), `bool TryFocus(IntPtr hwnd)` (ShowWindow SW_RESTORE + SetForegroundWindow), `int? GetWindowProcessId(IntPtr)`, `string? GetProcessName(int pid)`. Use classic `[DllImport]` for `EnumWindows` (delegate) and `[LibraryImport]` for the rest.
  - `WindowRegistry` (lock-protected, callable from any thread): `WindowInfo RegisterOrGet(IntPtr hwnd, string title, int pid, string? processName)`, `WindowInfo? Get(string handle)`, `void Prune(Func<IntPtr, bool> isAlive)`, `IReadOnlyList<WindowInfo> Snapshot()`.

- [ ] **Step 1: Write failing tests** (registry is pure): allocate handles monotonically per hwnd, re-registering the same hwnd returns the same handle but refreshes title; `Prune` removes dead hwnds; `Get` unknown → null; concurrent `RegisterOrGet`/`Snapshot` from 8 threads → no exceptions, all hwnds present.
- [ ] **Step 2: Run to fail.**
- [ ] **Step 3: Implement** (as specified; `Win32WindowApi` will be exercised by Task 17 integration tests).
- [ ] **Step 4: Run to pass.**
- [ ] **Step 5: Commit** — `git commit -m "feat(automation): win32 window api and thread-safe window registry"`

---

### Task 8: Session manager — launch, discovery, tracking, shutdown

**Files:**
- Create: `src/WinPilot.Automation/Sessions/SessionManager.cs`, `Sessions/LaunchService.cs`
- Test: `tests/WinPilot.Automation.Tests/Sessions/LaunchServiceTests.cs` (unit, injected fake window enumerator + fake process starter)

**Interfaces:**
- Consumes: `Win32WindowApi`, `WindowRegistry`, `WinPilotOptions`, exceptions.
- Produces:
  - `LaunchService` with injectable seams for tests: `Func<ProcessStartInfo, Process?> processStarter`, `Func<IReadOnlyList<(IntPtr Hwnd, string Title, int Pid)>> windowEnumerator`, `Func<TimeSpan, int?> delay` (default real sleep).
  - `Task<WindowInfo> LaunchAsync(string app, IReadOnlyList<string>? args, int? timeoutMs, CancellationToken ct)` — direct launches: `UseShellExecute = false`, track `Process`, poll enumerator for pid until a visible window appears or timeout → `LaunchFailedException("Timed out waiting for a window of process ...")`; UWP/shell task (`app` contains `!`): `explorer.exe shell:AppsFolder\{app}` + baseline-diff discovery of a new top-level window; explicit timeout honored.
  - `SessionManager`: `IReadOnlyList<WindowInfo> ListWindows()` (enumerator + registry register/prune), `WindowInfo Focus(string? handle, string? title)` (handle → `TryFocus`, else title → first match; fallback `WindowNotFound`), `void Close(string handle, bool force)` (WM_CLOSE + poll `CloseWindowTimeoutMs`; `force` and owned process → `Kill(entireProcessTree: true)`), `bool IsOwned(int pid)`, `void Shutdown(bool keepApps)` (closes owned processes), `Dispose()`.

- [ ] **Step 1: Write failing tests**:
  - launch discovers window on 3rd poll → returns info, process tracked;
  - launch timeout → `LaunchFailedException` with actionable hint;
  - UWP baseline-diff path finds a new window;
  - focus by title picks first match; unknown → `WindowNotFoundException`;
  - close: window disappears within timeout → success; simulated stubborn window without `force` → `OperationTimeoutException`; with `force`+owned → kill called once;
  - shutdown with `keepApps=false` closes owned apps, `true` leaves them; never touches non-owned processes.
- [ ] **Step 2: Run to fail.**
- [ ] **Step 3: Implement** (production defaults wire the real seams; keep `Process` objects in the manager).
- [ ] **Step 4: Run to pass.**
- [ ] **Step 5: Commit** — `git commit -m "feat(automation): session manager with launch tracking and shutdown policy"`

---

### Task 9: Element operations — click / type / fill / send keys / get text / wait for

**Files:**
- Create: `src/WinPilot.Automation/Input/SendKeysParser.cs`, `Input/KeyMap.cs`, `Input/KeyChord.cs`
- Create: `src/WinPilot.Automation/Elements/ElementOperations.cs`
- Test: `tests/WinPilot.Automation.Tests/Input/SendKeysParserTests.cs`

**Interfaces:**
- Consumes: registry, resolver, exceptions.
- Produces:
  - `KeyChord(IReadOnlyList<VirtualKeyShort> Modifiers, VirtualKeyShort Key)`; `SendKeysParser.Parse(string? chord, IReadOnlyList<string>? keys)` → `IReadOnlyList<KeyChord>`; rules: exactly one of `chord`/`keys`; chord = `+`-separated names, last token is the key, others are modifiers (`ctrl/control, shift, alt/menu, win/windows/meta`); unknown key → `InvalidArgumentException("Unknown key 'xyz'. Supported keys include: ...")`; key map identical to the original (letters, digits, F1-F12, navigation, media, backspace/delete/insert/esc/tab/space/enter/printscreen/pause).
  - `ElementOperations` (worker-confined, takes FlaUI elements): `string Click(AutomationElement el, MouseButtonKind button, bool doubleClick)` (priority Invoke → Toggle → SelectionItem → `GetClickablePoint` + `Mouse.Click/DoubleClick`; messages `Invoked X` / `Toggled X to On` / `Selected X` / `Clicked X` / `Double-clicked X`; name fallback to ref id), `string Type(string? refId, AutomationElement? el, string text, bool submit)` (focus + 50 ms settle under cancellation; `submit` → Enter), `string Fill(AutomationElement el, string value)` (Value pattern unless readonly → focus + Ctrl+A + type), `string SendKeys(AutomationElement? el, IReadOnlyList<KeyChord> chords)`, `string GetText(AutomationElement el)` (order Value → Selection → LegacyIAccessible → Name → Text), `ElementInfo WaitFor(Window window, string? refId, ElementSelector selector, TimeSpan timeout)` (poll 100 ms: exists && enabled && !offscreen; ref path returns fresh line + ref), `ElementSelector(string? Name, string? AutomationId, string? ControlType)`.
  - `ElementInfo(string Ref, string Line)`.

- [ ] **Step 1: Write failing tests** (parser + selector validation only; FlaUI execution is covered by Task 17):
  - `Parse("Ctrl+Shift+S", null)` → one chord `{Modifiers=[Control,Shift], Key=S}`;
  - `Parse(null, ["Ctrl+C","Down"])` → two chords; both `chord` and `keys` → `InvalidArgumentException`; neither → `InvalidArgumentException`;
  - `Parse("F5", null)`, `Parse("Media_Next", null)` work; `Parse("Ctrl+Foo", null)` throws with hint listing supported keys;
  - selector with zero constraints → `InvalidArgumentException`.
- [ ] **Step 2: Run to fail.**
- [ ] **Step 3: Implement** parser + operations.
- [ ] **Step 4: Run to pass.**
- [ ] **Step 5: Commit** — `git commit -m "feat(automation): element operations and send-keys parser"`

---

### Task 10: Capture service and save-path validation

**Files:**
- Create: `src/WinPilot.Automation/Capture/SavePathValidator.cs`, `Capture/CaptureService.cs`, `Capture/NativeWindowCapture.cs`, `Capture/ScreenshotResult.cs`
- Test: `tests/WinPilot.Automation.Tests/Capture/SavePathValidatorTests.cs`

**Interfaces:**
- Consumes: exceptions.
- Produces:
  - `SavePathValidator.TryNormalize(string? savePath, bool overwrite, out string? normalized, out string error)` — rules from the original: absolute local path, extension `.png`, reject UNC/device paths, reject existing file unless `overwrite`.
  - `ScreenshotResult(byte[] Png, string? SavedPath)`.
  - `CaptureService`: `CaptureScreen()`, `CaptureElement(AutomationElement)`, `NativeWindowCapture.TryCaptureWindow(IntPtr hwnd, out byte[] png)` (PrintWindow, blank-frame detection → caller falls back).
- [ ] **Step 1: Write failing tests**: accepts `C:\Temp\a.png`; rejects relative, UNC `\\srv\share\a.png`, device `\\?\C:\a.png`, non-png, empty; existing file without overwrite → error, with overwrite → ok; written file is byte-identical to input buffer.
- [ ] **Step 2: Run to fail.**
- [ ] **Step 3: Implement** (validation pure; capture adapted from the original `NativeWindowCapture`, MIT attribution comment).
- [ ] **Step 4: Run to pass.**
- [ ] **Step 5: Commit** — `git commit -m "feat(automation): capture service and save-path validation"`

---

### Task 11: IWindowsAutomation facade and engine wiring

**Files:**
- Create: `src/WinPilot.Automation/IWindowsAutomation.cs`, `Abstractions/Models.cs`, `WindowsAutomationEngine.cs`
- Modify: `src/WinPilot.Automation/Dispatcher/UiaContext.cs` (production context: `UIA3Automation` + `ElementRegistry`)
- Test: `tests/WinPilot.Automation.Tests/WindowsAutomationEngineTests.cs` with a **fake `IWindowsAutomation`** used by MCP-layer tests later (the fake itself is produced in Task 13; here: engine constructor wiring + disposal tests using injected fakes for dispatcher-level pieces).

**Interfaces — the agent-facing engine contract (freeze this; MCP tools depend on it):**

```csharp
public interface IWindowsAutomation : IAsyncDisposable
{
    Task<WindowInfo> LaunchAsync(string app, IReadOnlyList<string>? args, int? timeoutMs, CancellationToken ct);
    Task<IReadOnlyList<WindowInfo>> ListWindowsAsync(CancellationToken ct);
    Task<WindowInfo> FocusWindowAsync(string? handle, string? title, CancellationToken ct);
    Task CloseWindowAsync(string handle, bool force, CancellationToken ct);

    Task<string> SnapshotAsync(string? handle, int? maxDepth, CancellationToken ct);
    Task<ElementInfo> WaitForElementAsync(string? parentHandle, string? elementRef, ElementSelector? selector, int? timeoutMs, CancellationToken ct);

    Task<string> ClickAsync(string elementRef, string button, bool doubleClick, CancellationToken ct);
    Task<string> TypeAsync(string? elementRef, string text, bool submit, CancellationToken ct);
    Task<string> FillAsync(string elementRef, string value, CancellationToken ct);
    Task<string> SendKeysAsync(string? elementRef, string? chord, IReadOnlyList<string>? keys, CancellationToken ct);
    Task<string> GetTextAsync(string elementRef, CancellationToken ct);
    Task<ScreenshotResult> CaptureAsync(CaptureRequest request, CancellationToken ct);
}

public sealed record CaptureRequest(string? Handle, string? Ref, bool FullScreen, bool Background, string? SavePath, bool Overwrite);
public sealed record ElementSelector(string? Name, string? AutomationId, string? ControlType);
public sealed record ElementInfo(string Ref, string Line);
```

Behavior rules: `SnapshotAsync` bounds `maxDepth` to 20 and uses `WinPilotOptions` limits; foreground-window resolution when `handle` is null (UIA focused element walk-up on the worker); `WaitForElementAsync` requires `elementRef` xor `selector`; `CaptureAsync` uses the fast path for screen full-screen, worker for element/window; exceptions: engine throws `WinPilot*` types only.

- [ ] **Step 1: Write failing tests**: engine facade with a fake `UiaDispatcher` seam (internal `IUiaWorker` interface with `InvokeAsync` + `CurrentEpoch` declared in the engine assembly) verifies: `maxDepth` clamp to 20; invalid target (no handle/title) → `InvalidArgumentException`; `wait_for` with neither ref nor selector → `InvalidArgumentException`; dispose disposes session manager (owned apps closed when `KeepAppsOnExit=false`).
- [ ] **Step 2: Run to fail.**
- [ ] **Step 3: Implement** the facade: window-level calls → `SessionManager` directly (caller thread, locks); element-level calls → `dispatcher.InvokeAsync(...)` with production `UiaContext`; `UiaContext` disposes `UIA3Automation`, carries epoch + registry.
- [ ] **Step 4: Run to pass** + full `WinPilot.Automation.Tests`.
- [ ] **Step 5: Commit** — `git commit -m "feat(automation): IWindowsAutomation facade and engine wiring"`

---

### Task 12: MCP host bootstrap, stderr logging, stdout purity

**Files:**
- Create: `src/WinPilot.Mcp/Program.cs` (replace temporary), `src/WinPilot.Mcp/Configuration/OptionsBinding.cs`, `src/WinPilot.Mcp/Tools/DpiInitialization.cs`
- Test: `tests/WinPilot.Mcp.Tests/Tools/ToolErrorsTests.cs` (helper tests), integration later (Task 18).

**Interfaces:**
- Consumes: `IWindowsAutomation`, `WinPilotOptions`.
- Produces: `Program.cs` pattern below; `ToolErrors.ToErrorResult(WinPilotException ex)` → `CallToolResult { IsError = true, Content = [TextContentBlock { Text = $"{Code}: {Message} ({Hint})" }] }` + overload for unexpected exceptions (`UI_PROVIDER_ERROR` style generic); `DpiInitialization.EnablePerMonitorV2()`.

- [ ] **Step 1: Write failing test** for `ToolErrors`: formats `WINDOW_NOT_FOUND: Window not found: w9 (Run windows_list_windows...)`; unknown exception → `UI_PROVIDER_ERROR: <message> (Retry; if the problem persists, check the server log.)`; never throws on null hint.
- [ ] **Step 2: Run to fail.**
- [ ] **Step 3: Implement** `Program.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using WinPilot.Automation;
using WinPilot.Automation.Configuration;
using WinPilot.Mcp.Configuration;

var builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddEnvironmentVariables("WINPILOT_");         // WINPILOT_OPERATION_TIMEOUT_SECONDS → OperationTimeoutSeconds etc.
builder.Services.AddOptions<WinPilotOptions>()
    .Bind(builder.Configuration.GetSection(WinPilotOptions.SectionName))
    .Bind(builder.Configuration)                                   // env-var form (prefix stripped)
    .Validate(o => { o.Validate(); return true; });

builder.Logging.ClearProviders();
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);   // stdout stays protocol-only
var logFile = builder.Configuration["LOG_FILE"];                                  // WINPILOT_LOG_FILE
if (!string.IsNullOrEmpty(logFile)) builder.Logging.AddProvider(new FileLoggerProvider(logFile));

builder.Services.AddSingleton<IWindowsAutomation>(sp => new WindowsAutomationEngine(
    sp.GetRequiredService<IOptions<WinPilotOptions>>(),
    sp.GetRequiredService<ILoggerFactory>()));
builder.Services.AddMcpServer(o =>
    {
        o.ServerInfo = new() { Name = "winpilot", Version = ThisAssembly.Version(), Title = "WinPilot — Windows Desktop Automation" };
        o.ServerInstructions = "Use windows_snapshot to get the accessibility tree with element refs (w1e5). Act on refs with windows_click/type/fill/send_keys. Refs are invalidated by the next snapshot of that window; run windows_snapshot again to refresh.";
    })
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

DpiInitialization.EnablePerMonitorV2();
await builder.Build().RunAsync();
```

`FileLoggerProvider`: minimal thread-safe file logger (append, timestamped) — check `WINPILOT_LOG_FILE` handling; keep under 80 lines, dispose on shutdown.

> Exact SDK property names (`ServerInfo`, `ServerInstructions`, `StdioServerTransport`) must be confirmed against ModelContextProtocol 2.2.0 at implementation; if they differ, adjust here only (the tool layer is unaffected).

- [ ] **Step 4: Verify manually** — `dotnet run --project src/WinPilot.Mcp` then paste `{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"x","version":"1"}}}` + newline into stdin: response on stdout is pure JSON; logs, if any, on stderr. `Ctrl+C` exits cleanly.
- [ ] **Step 5: Commit** — `git commit -m "feat(mcp): host bootstrap with stderr-only logging and options binding"`

---

### Task 13: Window tools (launch, list, focus, close) + fake engine

**Files:**
- Create: `src/WinPilot.Mcp/Tools/WindowTools.cs`
- Create: `tests/WinPilot.Mcp.Tests/Fakes/FakeWindowsAutomation.cs` (the **shared fake** for Tasks 13-15: records calls, returns canned results, can throw configured `WinPilotException`s)
- Test: `tests/WinPilot.Mcp.Tests/Tools/WindowToolsTests.cs`

**Interfaces:**
- Consumes: `IWindowsAutomation`, `ToolErrors`.
- Produces: tool names `windows_launch`, `windows_list_windows`, `windows_focus`, `windows_close` with parameter/description contract from the spec §4.2; `FakeWindowsAutomation : IWindowsAutomation` with per-call queues/delegates and `List<RecordedCall>`.

- [ ] **Step 1: Write failing tests**:
  - `windows_launch` happy path → text contains `Window handle: w1` and `Title: ...`; `LaunchFailedException` → `CallToolResult.IsError == true` with `LAUNCH_FAILED`.
  - `windows_list_windows` returns one `- w1: "Calculator" (calc)` line per window; empty → `No windows found`.
  - `windows_focus` with neither handle nor title → error `INVALID_ARGUMENT`; fake throws `WindowNotFoundException` → mapped.
  - `windows_close` passes `force` through; fake called with `("w1", true)`.
- [ ] **Step 2: Run to fail.**
- [ ] **Step 3: Implement** `WindowTools` as an instance class with primary-constructor DI:

```csharp
[McpServerToolType]
public sealed class WindowTools(IWindowsAutomation automation)
{
    [McpServerTool(Name = "windows_launch"), Description("Launch a Windows application and return a window handle (w1) for use with the other tools.")]
    public async Task<CallToolResult> LaunchAsync(
        [Description("Executable path or UWP app id, e.g. 'calc.exe' or 'Microsoft.WindowsCalculator_8wekyb3d8bbwe!App'.")] string app,
        [Description("Optional command line arguments.")] IReadOnlyList<string>? args = null,
        [Description("Optional timeout in ms to wait for the app window (default 10000).")] int? timeoutMs = null,
        CancellationToken cancellationToken = default)
    { try { var w = await automation.LaunchAsync(app, args, timeoutMs, cancellationToken);
            return ToolErrors.Text($"Launched {app}\nWindow handle: {w.Handle}\nTitle: {w.Title}"); }
      catch (WinPilotException ex) { return ToolErrors.ToErrorResult(ex); } }
    // list/focus/close follow the same shape (list has no parameters; focus takes handle?, title?; close takes handle + force?).
}
```

- [ ] **Step 4: Run to pass.**
- [ ] **Step 5: Commit** — `git commit -m "feat(mcp): window tools with fake-engine tests"`

---

### Task 14: Element tools (snapshot, click, type, fill, send_keys, get_text)

**Files:**
- Create: `src/WinPilot.Mcp/Tools/ElementTools.cs`
- Test: `tests/WinPilot.Mcp.Tests/Tools/ElementToolsTests.cs`

**Interfaces:**
- Consumes: `IWindowsAutomation`, `FakeWindowsAutomation`.
- Produces: the six tools with the spec parameter contract; all return `CallToolResult` text or `isError`; `windows_snapshot { handle?, maxDepth? }` (no handle → foreground), `windows_click { ref, button?, doubleClick? }`, `windows_type { ref?, text, submit? }`, `windows_fill { ref, value }`, `windows_send_keys { ref?, chord?, keys? }`, `windows_get_text { ref }`.

- [ ] **Step 1: Write failing tests** (one test per tool + validation):
  - snapshot returns engine text verbatim; empty handle passes `null` to the engine;
  - click forwards `button` default `"left"` and `doubleClick` default `false`;
  - type with `submit=true` forwards; missing `text` → SDK-level validation is schema-driven, engine still guards: fake throws `InvalidArgumentException` → mapped;
  - send_keys forwards `chord` xor `keys`;
  - get_text returns raw text (empty string allowed — assert not treated as error);
  - every tool maps `WinPilotException` → error result with the exception's code.
- [ ] **Step 2: Run to fail.**
- [ ] **Step 3: Implement** (same shape as Task 13; `[Description]` text copied from spec §4.2).
- [ ] **Step 4: Run to pass.** Also assert the generated schema in one test: call the SDK's tool creation for each method and snapshot the JSON schema file (`tests/WinPilot.Mcp.Tests/Snapshots/schemas.snap.json`); fail on diff (catches accidental contract changes).
- [ ] **Step 5: Commit** — `git commit -m "feat(mcp): element tools with schema snapshots"`

---

### Task 15: Screenshot, wait_for, batch tools

**Files:**
- Create: `src/WinPilot.Mcp/Tools/ScreenshotTool.cs`, `Tools/WaitForTool.cs`, `Tools/BatchTool.cs`, `Tools/BatchModels.cs`, `Tools/BatchExecutor.cs`
- Test: `tests/WinPilot.Mcp.Tests/Tools/ScreenshotToolTests.cs`, `WaitForToolTests.cs`, `BatchExecutorTests.cs`

**Interfaces:**
- Consumes: `IWindowsAutomation`, `ToolErrors`.
- Produces:
  - `windows_screenshot { handle?, ref?, fullScreen?, background?, savePath?, overwrite? }` → image content block (`image/png`, base64) + text `Saved to <path>` when saved; validation error for `background` without handle/with ref/fullScreen (parity).
  - `windows_wait_for { ref?, handle?, name?, automationId?, controlType?, timeoutMs? }` → engine `ElementInfo.Line` + `Ref: <ref>`; requires ref xor selector.
  - `BatchAction` record `{ Action, Ref?, Text?, Value?, Ms?, Handle?, Chord?, Keys? }` and `BatchExecutor.RunAsync(actions, stopOnError, ct)` → numbered result lines `1. click: Invoked Seven` / `2. ERROR: ELEMENT_NOT_FOUND: ...` + `Stopped at action N due to error`; actions: `click, type, fill, wait, snapshot, sendKeys, getText`; `wait` uses `Task.Delay(ms, ct)`; unknown action → error line.
- [ ] **Step 1: Write failing tests**: batch sequential order via recorded fake calls; `stopOnError=false` continues; error line format; `wait` action honored; screenshot returns image block with `MimeType == "image/png"` (decode base64 → PNG magic bytes); background+ref → error; wait_for forwards selector and returns ref line.
- [ ] **Step 2: Run to fail.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run to pass** + full `WinPilot.Mcp.Tests` + schema snapshot update only if intentional.
- [ ] **Step 5: Commit** — `git commit -m "feat(mcp): screenshot, wait_for and batch tools"`

---

### Task 16: Test applications (WinForms + WPF)

**Files:**
- Create: `tests/TestApps/WinPilot.TestApps.WinForms/*` (Program.cs, MainForm.cs, csproj)
- Create: `tests/TestApps/WinPilot.TestApps.Wpf/*` (App.xaml(.cs), MainWindow.xaml(.cs), csproj)
- Modify: `tests/WinPilot.IntegrationTests/WinPilot.IntegrationTests.csproj` (ProjectReference to WinForms app)

**Interfaces:**
- Produces: WinForms app `WinPilot.TestApps.WinForms.exe` with CLI args `--hang-ms <n>` (button "Hang UI" blocks the UI thread for n ms), `--show-delay-ms <n>` (window shown after delay), and controls: `txtName`, `txtOutput`, buttons `btnHello` (sets `lblResult.Text = $"Hello {txtName.Text}"`), `btnShowLater` (after 800 ms shows `btnLate` with name "Late"), checkbox `chkOption`, radio `radA/radB`, combo `cboChoice` (items One/Two), list `lstItems` (Alpha/Beta), tree, tab control, menu item `mnuFile` with `mnuExit`, and unique AutomationIds on everything.
- WPF app: same core controls (Name in `[AutomationId]` not needed; UIA Name from `x:Name`/Content).

- [ ] **Step 1: Implement apps** (no tests of their own; they are fixtures).
- [ ] **Step 2: Verify** — build solution; launch WinForms app manually, confirm ids in FlaUI's inspect? Instead assert via a temporary smoke: `dotnet run --project tests/TestApps/WinPilot.TestApps.WinForms -- --show-delay-ms 500`.
- [ ] **Step 3: Commit** — `git commit -m "test: winforms and wpf test applications"`

---

### Task 17: Integration tests against real UIA

**Files:**
- Create: `tests/WinPilot.IntegrationTests/WindowsFactAttribute.cs` (skip when no interactive desktop: `Environment.UserInteractive` false or `GetForegroundWindow() == IntPtr.Zero`)
- Create: `tests/WinPilot.IntegrationTests/AutomationFixture.cs` (launches the WinForms test app, owns engine + options with short timeouts, disposes)
- Test: `tests/WinPilot.IntegrationTests/SnapshotIntegrationTests.cs`, `InteractionIntegrationTests.cs`, `WedgeRecoveryTests.cs`, `SessionIntegrationTests.cs`

**Interfaces:**
- Consumes: full `WindowsAutomationEngine`.
- Produces: real-UIA coverage of: snapshot format on the WinForms app (`window "..."`, `button "Hello" [ref=...]` lines), name fallback `[automationId]`, click → result label updated via `GetTextAsync`, type/fill/send_keys (incl. non-ASCII `héllo — 你好 🚀` in a textbox), wait_for (`btnLate` appears after 800 ms), stale ref after re-snapshot → `ElementStaleException`, stale ref auto-retry (button moved/recreated: click "Show Later" then re-click via old ref within same snapshot), screenshot PNG magic + savePath roundtrip, launch/close lifecycle (owned process closed on dispose), Win32 list windows includes the test app while a UIA call is wedged.

**Wedge test (the critical one):**
```
options: OperationTimeoutSeconds=1, HardTimeoutSeconds=2
1. launch app with --hang-ms 6000; click the Hang button (Invoke returns quickly).
2. snapshot → expect OperationTimeoutException within ~2 s.
3. meanwhile ListWindowsAsync() (Win32) still returns the app window — server-level responsiveness proof.
4. wait until hang elapsed (~6 s) → invoke again: dispatcher lazy-recycle triggers (current item exceeded hard deadline) →
   snapshot succeeds and returns the tree; assert engine epoch increased.
```
- [ ] **Step 1: Write tests** (as above).
- [ ] **Step 2: Run to fail** on the parts not yet wired (most will pass once fixtures exist; the wedge test must initially fail if recycle is not correctly triggered — verify by running it against the dispatcher without recycle first if needed).
- [ ] **Step 3: Fix engine defects exposed.**
- [ ] **Step 4: Run to pass** — `dotnet test tests/WinPilot.IntegrationTests` (local interactive session).
- [ ] **Step 5: Commit** — `git commit -m "test: real-uia integration suite including wedge recovery"`

---

### Task 18: Protocol E2E over stdio — the anti-disconnect regression suite

**Files:**
- Create: `tests/WinPilot.IntegrationTests/Protocol/RawMcpClient.cs` (spawns `dotnet exec <WinPilot.Mcp.dll>` with redirected stdio; `SendRawAsync(string)`; `ReadLineAsync(TimeSpan)`; JSON helpers; **records every stdout line** for purity assertions)
- Create: `tests/WinPilot.IntegrationTests/Protocol/StdioProtocolTests.cs`
- Test: uses `ModelContextProtocol.Client` via `McpClient.CreateAsync(new StdioClientTransport(...))` for the happy-path workflow (verify exact client API at implementation).

**Interfaces:**
- Consumes: the built server assembly path (from the ProjectReference: `typeof(Program).Assembly.Location` if visible; else locate `winpilot-mcp.dll` next to the test output).
- Produces: tests:
  1. `Initialize_handshake_completes_under_5s` (measures wall time of the `initialize` round-trip).
  2. `Ping_is_answered_within_2s_while_a_long_tool_call_is_running` — start `windows_batch` with `{"action":"wait","ms":4000}`; while pending, send raw `{"jsonrpc":"2.0","id":99,"method":"ping"}`; assert a response with `id:99` arrives < 2 s and the batch call later completes.
  3. `Cancelled_tool_returns_promptly_spontaneously` — start a `wait` batch (10 s), send `notifications/cancelled` for its request id; expect a response for that id within 2 s (`isError` or cancelled); server still answers `ping` after.
  4. `Twenty_consecutive_tool_errors_do_not_kill_the_server` — 20 × `windows_get_text` with `ref: "w99e99"`; each returns `isError`; final `ping` answered.
  5. `Every_stdout_line_is_valid_jsonrpc_during_full_workflow` — launch `notepad.exe` (or the test app), snapshot, click nothing, close; assert every captured stdout line parses as JSON with `jsonrpc: "2.0"`.
  6. `Concurrent_tool_calls_are_serialized_and_both_succeed` — two raw `tools/call` in parallel (e.g., two `windows_list_windows`) → both responses arrive, ids correct.
  7. `Graceful_shutdown_closes_launched_apps` — launch test app via tool; close server stdin; assert process exits within 5 s and the app process is gone.
- [ ] **Step 1: Write tests.**
- [ ] **Step 2: Run to fail/pass cycle; fix any protocol issues found.**
- [ ] **Step 3: Run to pass** — full integration suite.
- [ ] **Step 4: Commit** — `git commit -m "test: stdio protocol e2e anti-disconnect regression suite"`

---

### Task 19: CI workflow

**Files:**
- Create: `.github/workflows/ci.yml`

**Interfaces:** none (CI).
- [ ] **Step 1: Write `ci.yml`**: `windows-latest`, `actions/setup-dotnet@v4` with `10.0.x`; steps: restore, `dotnet format WinPilot.Mcp.slnx --verify-no-changes`, build `-c Release`, unit tests (`WinPilot.Automation.Tests`, `WinPilot.Mcp.Tests`), integration tests (`WinPilot.IntegrationTests`; auto-skips without desktop), `dotnet pack src/WinPilot.Mcp -c Release`. Upload the nupkg artifact.
- [ ] **Step 2: Validate syntax** via `actionlint` if available, else careful review; commit — `git commit -m "ci: build, format, unit and integration tests"`
- [ ] **Step 3:** If integration tests prove flaky on hosted runners, split them into a separate `integration.yml` marked `continue-on-error: true` and note it in README (decision recorded in commit message).

---

### Task 20: Release workflow and packaging

**Files:**
- Create: `.github/workflows/release.yml`
- Modify: `README.md` will carry install instructions (Task 21).

**Interfaces:** MinVer reads `v*` tags; version flows into assembly + package.
- [ ] **Step 1: Write `release.yml`**: trigger `push: tags: ['v*']`; full test suite; `dotnet pack src/WinPilot.Mcp -c Release -o artifacts`; for `win-x64` and `win-arm64`: `dotnet publish src/WinPilot.Mcp -c Release -r <rid> --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish/<rid>` + `Compress-Archive` to `WinPilot.Mcp-<rid>-<version>.zip`; create GitHub Release via `gh release create "${{ github.ref_name }}" ... --generate-notes` with all artifacts.
- [ ] **Step 2: Validate locally what can be validated**: run the pack + one RID publish command from the workflow by hand; confirm `dotnet tool install --global --add-source ./artifacts WinPilot.Mcp` works and `winpilot-mcp` starts.
- [ ] **Step 3: Commit** — `git commit -m "ci: release workflow with tool package and self-contained zips"`

---

### Task 21: Documentation, license, and final polish

**Files:**
- Create: `README.md` (replace), `docs/architecture.md`, `CHANGELOG.md`, `CONTRIBUTING.md` (short)
- Create: `LICENSE` (MIT, dual attribution) 
- Modify: sources for XML doc gaps if `dotnet build` reports warnings.

**Interfaces:** none.
- [ ] **Step 1: Verify install snippets on this machine** (authoritative):
  - `dotnet dnx --help` → record the exact zero-install invocation and whether a confirmation flag is needed; write the verified opencode snippet.
  - `dotnet tool install -g WinPilot.Mcp` against the local package then `winpilot-mcp` smoke run.
- [ ] **Step 2: Write README** (English): what it is, why (snapshot/ref model), quick demo (launch → snapshot → batch click), install section with three paths (dotnet tool / dnx / zip), **opencode config first** (`"mcp": { "winpilot": { "type": "local", "command": ["winpilot-mcp"] } }`), VS Code/Copilot and Claude configs, tool table (13 tools incl. `windows_wait_for`), configuration env-var table, troubleshooting (opencode connect timeout 30 s, `"timeout"` override; logs via `WINPILOT_LOG_FILE`), safety limitations (focus-dependent keyboard input, background capture caveats), supported apps matrix, license/credits.
- [ ] **Step 3: `docs/architecture.md`**: the dispatcher/timeouts/recycle model, Win32 vs UIA split, error taxonomy, testing strategy, spec link.
- [ ] **Step 4: `LICENSE` + credits**: MIT text with "Portions copyright (c) Scott Hanselman / FlaUI-MCP" and FlaUI attribution; `CHANGELOG.md` with `0.1.0` entry; short `CONTRIBUTING.md` (build/test commands).
- [ ] **Step 5: Final verification** — `dotnet build -c Release`, all tests, `dotnet format --verify-no-changes`; fix and commit — `git commit -m "docs: readme, architecture, license and changelog"`.

---

## Self-Review (performed)

- **Spec coverage:** §2 structure → T1; §3.1 dispatcher → T3; §3.2 Win32 → T7; §3.3 sessions → T8; §3.4 refs → T4 (+T9 retry); §3.5 snapshot → T5/T6; §3.6 input/capture → T9/T10; §3.7 errors → T2; §3.8 options → T2/T12; §4 host → T12; §4.2 tools → T13-T15; §4.3 batch → T15; §5 tests (unit/tool/integration/E2E) → T3-T11, T17, T18; §6 CI/release/distribution → T19/T20/T21; §7 licensing → T21; Review Focus items → T3, T17 (wedge), T4/T9 (stale), T5/T17 (hostile names), T18 (concurrency, startup/purity).
- **Placeholder scan:** no TBD/TODO; the two "verify at implementation" notes (SDK 2.2.0 option names in T12; client API in T18) are explicit verification steps, not missing content.
- **Type consistency:** `IWindowsAutomation` signatures in T11 are used verbatim by T13-T15 and fakes; `ElementInfo`, `ElementSelector`, `CaptureRequest`, `SnapshotNode`, `ElementFingerprint` names are consistent across tasks; timeouts/options names match T2.
