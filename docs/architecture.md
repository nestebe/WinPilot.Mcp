# Architecture

WinPilot.Mcp is two projects with a one-way dependency:

```
WinPilot.Mcp (console exe)          MCP stdio host + tools (ModelContextProtocol SDK)
        │  depends on
        ▼
WinPilot.Automation (class library)  UI Automation engine (FlaUI) — no MCP knowledge
```

- `WinPilot.Mcp` only validates input, calls the engine, and formats results. Tools are `[McpServerTool]` methods with DI-injected `IWindowsAutomation`; JSON schemas are generated from the method signatures.
- `WinPilot.Automation` owns everything else and is testable without MCP.

## The threading model (the core reliability fix)

All UI Automation work runs on **one dedicated MTA worker thread** managed by `UiaDispatcher<TContext>`:

- Public engine calls enqueue a work item (delegate, cancellation token, completion source) and await it.
- A single worker executes items in FIFO order; **no UIA call can ever run outside this thread**, so concurrent COM access is impossible by construction.
- **Soft timeout** (default 30 s): the caller gets `OPERATION_TIMEOUT`; the in-flight UIA call cannot be aborted mid-call, so its eventual result is discarded and logged.
- **Hard timeout** (default 90 s): when a new request arrives while the worker has been busy past the hard deadline, the dispatcher **recycles**: new thread, new `UIA3Automation` context, epoch increment. Refs from before the recycle report `ELEMENT_STALE`.
- A new request that arrives while the worker is past the soft timeout fails fast with `ENGINE_BUSY` instead of queueing indefinitely.

Because the MCP host never awaits UIA work synchronously on the protocol path, the server always answers `ping` and `notifications/cancelled` — the root cause of the original server's disconnects.

## Win32 fast paths vs UI Automation

Window-level operations don't need UIA and run on the caller thread under lock-protected state:

| Operation | Mechanism |
| --- | --- |
| `windows_list_windows` | `EnumWindows` + title/pid, skipping invisible, tool, and DWM-cloaked windows |
| `windows_focus` | `ShowWindow(SW_RESTORE)` + `SetForegroundWindow` |
| `windows_close` | `PostMessage(WM_CLOSE)` + wait; optional kill of owned processes |
| Launch discovery | Poll `EnumWindows` for the launched PID (or baseline diff for UWP) |

These keep working while the UIA worker is wedged — verified by the wedge integration test.

## Elements, refs, and snapshots

- Refs are `w{n}` (windows) and `w{n}e{k}` (elements); the format is a stable agent-facing contract.
- `ElementRegistry<TElement>` keeps the current snapshot per window plus one previous version, so a stale ref can be told apart from an unknown one and re-resolved by fingerprint (`AutomationId` → `RuntimeId` → `Name` + `ControlType`) — one retry, then `ELEMENT_STALE`.
- Snapshots are built by a pure formatter (`SnapshotFormatter`) over a `SnapshotNode` tree produced by `SnapshotWalker` over `IUiNode` — production adapts FlaUI (`FlaUiNode`), tests use fake trees. Depth, node, and time budgets produce explicit truncation markers.

## Error taxonomy

Engine failures are typed (`WinPilotException` + code) and become `isError` tool results with a SCREAMING_SNAKE code, a message, and an actionable hint:

`INVALID_ARGUMENT`, `WINDOW_NOT_FOUND`, `ELEMENT_NOT_FOUND`, `ELEMENT_STALE`, `OPERATION_TIMEOUT`, `ENGINE_BUSY`, `ENGINE_UNAVAILABLE`, `LAUNCH_FAILED`, `CAPTURE_FAILED`, `UI_PROVIDER_ERROR`, `NOT_SUPPORTED`.

## Logging and stdout purity

stdout carries JSON-RPC only. Logs go to stderr (console sink with `LogToStandardErrorThreshold`), optionally to a file (`WINPILOT_LOG_FILE`). The E2E suite asserts that every stdout line parses as JSON-RPC during a full workflow.

## Testing pyramid

1. **Unit** (`WinPilot.Automation.Tests`): dispatcher behavior (ordering, soft/hard timeouts, recycle, busy, cancellation), refs/registry, formatter, walker with fake trees, send-keys parser, save-path validation, options.
2. **Tool layer** (`WinPilot.Mcp.Tests`): tools against a scriptable fake engine — validation, error mapping, output formats.
3. **Integration** (`WinPilot.IntegrationTests`): real UIA against a WinForms fixture app — snapshot/interaction/wait/screenshot, stale re-resolution, wedge recovery (soft timeout, Win32 availability, recovery), session lifecycle.
4. **Protocol E2E**: the real server over stdio — ping during long operations, cancellation, 20-error storms, stdout purity, concurrent calls, graceful shutdown, and a committed schema snapshot of all tools.

See `docs/superpowers/specs/2026-09-28-winpilot-mcp-design.md` for the full design and `docs/superpowers/plans/2026-09-28-winpilot-mcp.md` for the implementation plan.
