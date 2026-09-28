# WinPilot.Mcp — Design Specification

- **Date:** 2026-09-28
- **Status:** Approved for implementation (design reviewed by the project owner; autonomous execution granted)
- **Provenance:** Re-architecture of [shanselman/FlaUI-MCP](https://github.com/shanselman/FlaUI-MCP) (MIT). Feature parity is preserved for the `windows_*` tool surface.

## 1. Problem Statement

The original FlaUI-MCP server repeatedly disconnects from MCP clients (reported with opencode as the primary client). Root causes identified by reading the original source:

1. **Blocked protocol loop.** The hand-rolled JSON-RPC loop awaits each tool call serially. While a tool runs (up to 30 s+ of UI Automation work), it cannot answer client `ping` requests or cancellation notifications. Clients treat a silent server as dead and drop the connection.
2. **Fake timeouts with orphaned work.** `Task.WhenAny(toolTask, timeoutTask)` returns a timeout error to the client while the UIA work keeps running on a background thread. Orphaned UIA calls run concurrently with subsequent calls against the same `UIA3Automation` instance, which is not safe: blocked threads accumulate, COM state can be corrupted, and the process can hang or crash. A crash closes stdio and the client shows "Connection closed".
3. **Incomplete hand-rolled MCP.** Hardcoded protocol version `2024-11-05`, no `ping`, unknown methods answered with errors (including notifications, which JSON-RPC forbids answering), no version negotiation, no cancellation support.
4. **Non-thread-safe state.** `ElementRegistry` and `SessionManager` use plain `Dictionary` instances mutated from multiple threads.
5. **State leaks.** `_windows` grows forever, `_applications` is never populated (dead cleanup path), the `UIA3Automation` instance is never recycled. One wedged UIA provider call freezes the server permanently.

Secondary issues: `Thread.Sleep(1000)` heuristics at launch, `UseShellExecute` launch path, duplicated click/type/fill logic between individual tools and `windows_batch`, inconsistent namespaces (`PlaywrightWindows.Mcp` in some files), `System.Text.Json 10.x` referenced from a `net8.0-windows` project.

### Goals

- **Zero disconnections** under opencode and other MCP clients: the protocol layer must stay responsive at all times; the process must not crash on UIA faults.
- **Maintainability over the long term**: layered architecture, single responsibility per unit, testable seams, .NET best practices, CI/CD.
- **Functional parity** with the original 12 `windows_*` tools, plus a small set of targeted additions.
- **Simplest possible installation**, with opencode as the primary target client.

### Non-goals (v1)

- HTTP/SSE transport, multi-user sessions, recording/replay, visual annotation of screenshots, Electron-specific quirks.
- Separate driver process (design keeps the door open: the automation engine is isolated behind interfaces, so a future out-of-process engine would not change the MCP tool layer).

## 2. Solution Structure

```
WinPilot.Mcp.slnx
├─ Directory.Build.props        # shared build settings
├─ Directory.Packages.props     # central package management
├─ global.json                  # pin SDK 10.0.401 (rollForward: latestFeature)
├─ .editorconfig
├─ .gitignore
├─ LICENSE (MIT + attribution)
├─ README.md
├─ CHANGELOG.md
├─ docs/
│  ├─ architecture.md
│  └─ superpowers/ (specs, plans)
├─ src/
│  ├─ WinPilot.Automation/      # class library, net10.0-windows, FlaUI only
│  └─ WinPilot.Mcp/             # console exe, MCP host + tools, ModelContextProtocol
├─ tests/
│  ├─ WinPilot.Automation.Tests/
│  ├─ WinPilot.Mcp.Tests/
│  ├─ WinPilot.IntegrationTests/
│  └─ TestApps/
│     ├─ WinPilot.TestApps.WinForms/
│     └─ WinPilot.TestApps.Wpf/
└─ .github/workflows/ (ci.yml, release.yml)
```

**Dependency rule:** `WinPilot.Mcp` → `WinPilot.Automation`, never the reverse. The engine has no knowledge of MCP; the host only validates input, calls the engine, and formats results.

**Shared build policy (`Directory.Build.props`):**

- `TargetFramework: net10.0-windows`, `LangVersion: latest`, `Nullable: enable`, `ImplicitUsings: enable`.
- `TreatWarningsAsErrors: true`, `AnalysisLevel: latest-recommended`, `EnforceCodeStyleInBuild: true`.
- `InvariantGlobalization` not set (culture affects UI Automation text handling).
- No StyleCop (opinionated noise); built-in analyzers + `.editorconfig`, enforced by `dotnet format --verify-no-changes` in CI.

**Package versions (centralized, pinned at implementation time):**

- `FlaUI.Core`, `FlaUI.UIA3` 5.0.0 (net8.0-windows assets run fine on net10.0-windows).
- `ModelContextProtocol` 2.2.0 (official C# SDK).
- Microsoft.Extensions.* (Hosting, Options, Logging) matching the .NET 10 BCL.
- `MinVer` for tag-driven versioning.
- Tests: xunit v3 family + Microsoft testing platform integration.

## 3. Automation Engine (`WinPilot.Automation`)

### 3.1 Threading model — the core reliability fix

`UiaDispatcher` owns a **single dedicated background thread (MTA — required for UIA3)** and a FIFO `Channel<WorkItem>`. Every public engine operation is a `Task<T>` that enqueues a work item carrying:

- the delegate to run (only ever executed on the worker thread),
- a client `CancellationToken` (MCP cancellation),
- soft/hard deadlines,
- a `TaskCompletionSource<T>`.

**Invariants:**

- All UIA element work is serialized on the worker; no concurrent UIA access exists by construction. Window-level operations (section 3.2) do not use UIA and run on the caller thread under lock-protected state.
- The MCP protocol layer never awaits UIA work synchronously: it awaits `Task`s while the SDK transport keeps reading stdin and answering `ping`/`notifications`.

**Two-level timeouts:**

- *Soft timeout* (default 30 s, `WINPILOT_OPERATION_TIMEOUT_SECONDS`): when exceeded, the caller receives a `OPERATION_TIMEOUT` error immediately. The in-flight UIA call cannot be aborted mid-call (COM/UIA limitation); its eventual result is discarded and logged.
- *Hard timeout* (default 90 s, `WINPILOT_HARD_TIMEOUT_SECONDS`): when exceeded, the dispatcher **recycles**: it starts a fresh worker thread and a fresh `UIA3Automation` instance, bumps the engine epoch (invalidating all outstanding refs), and abandons the old thread (which only still references its own objects; its completion is observed and ignored). New requests are served by the new worker.
- Queued work whose wait exceeds the soft timeout before starting fails fast with `ENGINE_BUSY`.

**Cancellation:** cooperative, checked at every operation boundary (before/after each UIA call, at each snapshot node, in wait loops). Cancellation is not converted into an error text — the `OperationCanceledException` is propagated so the SDK answers the MCP cancellation semantics natively.

### 3.2 Win32 fast paths

Window-level operations do not need UIA; they use native Win32 and run **off the UIA worker** under lock-protected state, so they remain available even while a UIA call is wedged:

- `windows_list_windows`: `EnumWindows` + `GetWindowText` + `GetWindowThreadProcessId`, skipping invisible, tool (`WS_EX_TOOLWINDOW`) and DWM-cloaked windows. Never blocks on providers.
- `windows_close`: `PostMessage(WM_CLOSE)` + wait-until-gone; optional `force` kills the process tree if the app was launched by this server.
- `windows_focus`: `ShowWindow(SW_RESTORE)` + `SetForegroundWindow`, fallback to UIA `Focus()` on the worker.
- Launch window discovery: poll `EnumWindows` for the launched PID (direct launch) with a timeout (default 10 s, `WINPILOT_LAUNCH_WINDOW_TIMEOUT_MS`), 100 ms interval, honoring cancellation. UWP/shell launches fall back to baseline diffing of visible windows.

### 3.3 Sessions, windows, processes

`SessionManager` (engine-internal, injected as `IWindowsAutomation` façade):

- Tracks applications **launched by this server** (`Process` objects + PID), unlike the original.
- Window handles: `w{n}` (monotonic per session), resolvable from the live `hwnd`.
- Dead windows are pruned on every window-level operation and at registration time (no unbounded growth).
- On graceful shutdown, apps launched by this server are closed; opt-out via `WINPILOT_KEEP_APPS_ON_EXIT=1`.
- Engine epoch: incremented on recycle; stale refs from an older epoch fail fast with an actionable `ELEMENT_STALE`.

### 3.4 Element references

- Format unchanged: `w{window}e{index}` (e.g. `w1e5`), regenerated on every snapshot of that window (parity with the original lifecycle).
- Registry stores element + fingerprint: `ControlType`, `AutomationId`, `Name`, and `RuntimeId` when available.
- **Stale-ref recovery (one retry):** if an element is unavailable when used, re-resolve within the same window by `AutomationId`, else `Name` + `ControlType`; if found, rebind and execute the operation once. Otherwise fail with `ELEMENT_STALE` + "run windows_snapshot to refresh refs".
- The element registry is only mutated on the worker thread. Window metadata (hwnd, title, PID) lives in a separate lock-protected registry used by the Win32 fast paths. No plain shared `Dictionary` anywhere.

### 3.5 Snapshot pipeline

Two units with a clean seam:

1. `SnapshotWalker` (FlaUI-dependent): walks the tree and produces a `SnapshotNode` DTO (role, name, ref, states, children) with skip rules identical to the original.
2. `SnapshotFormatter` (pure): `SnapshotNode` + options → text. 100 % unit-testable without Windows.

**Format (parity):**

```
- window "Calculator" [ref=w1]
  - button "Seven" [ref=w1e4] [disabled]
```

- Role mapping table identical to the original.
- Name fallback to `[automationId]` when the name is empty; escaping of `\`, `"`, newlines.
- State indicators: `disabled`, `offscreen`, `readonly`, `checked`, `indeterminate`, `selected`, `expanded`, `collapsed`.

**Guard rails:** `MaxDepth` (default 10; tool override up to 20), `MaxNodes` (default 2000), `TimeBudgetMs` (default 10 000). On truncation, explicit markers are appended as comment lines: `... (truncated: depth limit)`, `... (truncated: node limit reached)`, `... (truncated: time budget exceeded)`.

### 3.6 Input and capture

- Keyboard: pure, unit-testable `SendKeysParser` (chord `Ctrl+Shift+S`, sequence, key map identical to the original including media keys) + thin FlaUI `Keyboard` execution.
- Mouse: pattern-first click priority unchanged (`Invoke` → `Toggle` → `SelectionItem` → mouse); `GetClickablePoint` fallback with scroll-into-view.
- Screenshot: screen/window/element capture; optional native background capture with blank-frame fallback; strict `savePath` validation (absolute local `.png`, no UNC/device paths, no silent overwrite) — rules preserved from the original.

### 3.7 Error taxonomy

Typed engine exceptions with stable code prefixes, mapped to actionable single-line MCP error text:

| Code | Meaning |
| --- | --- |
| `INVALID_ARGUMENT` | Bad tool input (validated before touching UIA) |
| `WINDOW_NOT_FOUND` | Unknown/dead window handle |
| `ELEMENT_NOT_FOUND` | Unknown ref |
| `ELEMENT_STALE` | Ref no longer resolvable, re-snapshot required |
| `OPERATION_TIMEOUT` | Soft timeout exceeded |
| `ENGINE_BUSY` | Queue wait exceeded soft timeout |
| `ENGINE_UNAVAILABLE` | Worker recycle in progress; retry shortly |
| `LAUNCH_FAILED` | Process start or window discovery failed |
| `CAPTURE_FAILED` | Screenshot path failed |
| `UI_PROVIDER_ERROR` | Underlying UIA/COM provider error (wrapped) |

### 3.8 Settings

Bound via `IOptions` from optional `appsettings.json` + environment variables (`WINPILOT_` prefix):

| Setting | Env var | Default |
| --- | --- | --- |
| OperationTimeoutSeconds | `WINPILOT_OPERATION_TIMEOUT_SECONDS` | 30 |
| HardTimeoutSeconds | `WINPILOT_HARD_TIMEOUT_SECONDS` | 90 |
| SnapshotMaxDepth | `WINPILOT_SNAPSHOT_MAX_DEPTH` | 10 |
| SnapshotMaxNodes | `WINPILOT_SNAPSHOT_MAX_NODES` | 2000 |
| SnapshotTimeBudgetMs | `WINPILOT_SNAPSHOT_TIME_BUDGET_MS` | 10000 |
| LaunchWindowTimeoutMs | `WINPILOT_LAUNCH_WINDOW_TIMEOUT_MS` | 10000 |
| KeepAppsOnExit | `WINPILOT_KEEP_APPS_ON_EXIT` | false |
| LogLevel | `WINPILOT_LOG_LEVEL` | Warning |
| LogFile | `WINPILOT_LOG_FILE` | (none) |

## 4. MCP Host (`WinPilot.Mcp`)

### 4.1 Host and logging

- `Host.CreateApplicationBuilder` + `AddMcpServer().WithStdioServerTransport().WithToolsFromAssembly()`; server name `winpilot`; version from assembly (MinVer); server `instructions` describing the snapshot → ref → act workflow.
- Logging: console sink configured with `LogToStandardErrorThreshold = Trace` so **stdout carries only JSON-RPC**; optional file sink from `WINPILOT_LOG_FILE`; level from `WINPILOT_LOG_LEVEL`. Rule: no `Console.Write*` anywhere in the codebase (enforced by review + the E2E stdout-purity test).
- Tool methods: `[McpServerToolType]` classes receiving `IWindowsAutomation` and `IOptions<>` via DI; parameters are typed records with `[Description]` → JSON schemas auto-generated by the SDK; every method takes `CancellationToken`.

### 4.2 Tool contract

| Tool | Parameters | Notes |
| --- | --- | --- |
| `windows_launch` | `app`, `args?`, `timeoutMs?` | Returns handle, title, PID |
| `windows_snapshot` | `handle?`, `maxDepth?` | Foreground window when handle omitted |
| `windows_click` | `ref`, `button?`, `doubleClick?` | Pattern-first, mouse fallback |
| `windows_type` | `ref?`, `text`, `submit?` | Focuses ref first when provided |
| `windows_fill` | `ref`, `value` | Value pattern, fallback select-all + type |
| `windows_send_keys` | `ref?`, `chord?` xor `keys?` | Same key map as original |
| `windows_get_text` | `ref` | Value > Selection > LegacyIAccessible > Name > Text pattern |
| `windows_screenshot` | `handle?`, `ref?`, `fullScreen?`, `background?`, `savePath?`, `overwrite?` | Image content (+ saved path) |
| `windows_list_windows` | — | Win32 fast path; refreshes/prunes registry |
| `windows_focus` | `handle?` xor `title?` | Win32 fast path |
| `windows_close` | `handle`, `force?` | WM_CLOSE; `force` kills launched apps |
| `windows_batch` | `actions[]`, `stopOnError?` | Actions: click, type, fill, wait, snapshot, **sendKeys**, **getText** |
| `windows_wait_for` | `ref?` or (`handle?` + `name?`/`automationId?`/`controlType?`), `timeoutMs?` | Waits for an element to exist and be enabled + on-screen; returns its snapshot line with a fresh ref |

Result content: text for everything, image content for screenshots; errors are `isError: true` with `CODE: message (hint)` text.

### 4.3 Batch semantics

Actions run sequentially through the same engine API as the individual tools (no duplicated logic — the original duplicated click/type/fill). `stopOnError` default true. Each action is individually timed; results are numbered lines matching the original format.

## 5. Testing Strategy

1. **Unit (no UI):** snapshot formatter, skip rules, truncation; SendKeys parser; ref formatting/fingerprints; options validation; dispatcher behaviors with fake work items (ordering, cancellation, soft timeout returns while worker continues, hard timeout recycles, queued item fails fast).
2. **Tool layer (`WinPilot.Mcp.Tests`):** tools invoked directly with a fake `IWindowsAutomation`; asserts parameter validation, error mapping, output formats, and snapshots of generated JSON schemas (catches accidental breaking changes to the agent-facing contract).
3. **Integration (real UIA, skipped when no interactive desktop):** WinForms/WPF test apps; snapshot/click/type/fill/get_text/screenshot flows; UWP Calculator smoke test; stale-ref recovery; a **"hang" button** in the WinForms app that freezes its message pump — validates real provider wedging without production test hooks: soft timeout returns, server stays responsive (window listing still works), hard timeout recycles, operations succeed after the hang ends.
4. **Protocol E2E over stdio** (official SDK client + a raw line driver for edge cases) — the disconnect regression suite:
   - a `ping` (raw line) is answered in < 2 s while a long tool call (batch wait) is in flight;
   - in-flight tool cancellation returns promptly (`notifications/cancelled`);
   - 20 consecutive failing tool calls leave the server healthy;
   - every stdout line parses as JSON-RPC during a complete workflow (purity test);
   - graceful shutdown closes launched apps.

## 6. Build, CI, Release, Distribution

- `global.json` pins .NET SDK 10.0.401 (`rollForward: latestFeature`).
- `ci.yml`: windows-latest → restore (locked), build with analyzers, `dotnet format --verify-no-changes`, unit + tool tests, integration + E2E tests, `dotnet pack`.
- `release.yml`: on tags `v*` → full test suite → `dotnet pack` (NuGet tool package) → self-contained single-file publishes (`win-x64`, `win-arm64`, native libs self-extract, not trimmed) → GitHub Release with assets and notes.
- Versioning: MinVer, tag prefix `v`, floor `0.1`.
- **Install paths documented in README:**
  1. `dotnet tool install -g WinPilot.Mcp` → opencode config `"command": ["winpilot-mcp"]` (primary path);
  2. `dnx` zero-install variant (exact invocation verified and documented at implementation time);
  3. self-contained zip for machines without .NET.
- opencode caveat documented: connect/tool-listing timeout default 30 s; first-start cost (tool restore) must fit or the user raises `"timeout"` in the MCP config.

## 7. Licensing and Attribution

MIT. `LICENSE` keeps the original copyright notice; `README`/`NOTICE` credit FlaUI-MCP (Scott Hanselman, MIT) and FlaUI (MIT). Adapted test apps from the original repository retain attribution.

## 8. Risks and Mitigations

| Risk | Mitigation |
| --- | --- |
| A UIA call wedged forever (blocking COM) | Serialized worker + hard-timeout recycle; protocol layer unaffected; Windows fast paths keep window-level tools working |
| Worker recycle leaves a COM object in a bad state | Old thread is fully abandoned; new `UIA3Automation` + epoch bump isolate it; covered by the wedge integration test |
| UWP launch heuristics | PID-based discovery for direct launches; baseline diff fallback; explicit timeout error with guidance |
| UI tests flaky or unavailable in CI | Integration tests auto-skip without an interactive desktop; unit + protocol tests (fake engine) always run |
| `dnx`/opencode integration specifics drift | Verified against current opencode docs at implementation time; README pins working snippets |

## 9. Decisions Log (autonomous calls)

- **Approach A** chosen: in-process serialized engine (simple, testable); out-of-process engine remains a possible evolution because the engine is isolated behind interfaces.
- Win32 fast paths for window-level operations (reduces UIA blocking surface).
- `windows_wait_for` added; `force` on close; `sendKeys`/`getText` in batch.
- xunit v3 family; MinVer; CPM; warnings-as-errors; no StyleCop.
- No production test hooks: wedging is tested with the real test-app "hang" button.
- Defaults: soft 30 s / hard 90 s; snapshot depth 10 / nodes 2000 / budget 10 s; launch window timeout 10 s.
