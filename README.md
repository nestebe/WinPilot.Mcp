# WinPilot.Mcp

[![CI](https://github.com/nestebe/WinPilot.Mcp/actions/workflows/ci.yml/badge.svg)](https://github.com/nestebe/WinPilot.Mcp/actions/workflows/ci.yml)

An MCP (Model Context Protocol) server that lets AI agents automate Windows desktop applications through accessibility APIs — the same way Playwright automates browsers.

`windows_snapshot` returns a structured accessibility tree with element refs (`w1e5`); agents act on refs with `windows_click`, `windows_type`, `windows_fill`, and friends. No screenshot guessing, no coordinates.

WinPilot.Mcp gives AI agents a Playwright-style workflow for Windows desktop apps: `windows_snapshot` returns a structured accessibility tree with element refs (`w1e5`), and agents act on those refs — no screenshot guessing, no coordinates. One hard requirement: **never disconnect**. All UI Automation work is serialized on a dedicated worker thread, the MCP protocol layer always stays responsive, and timeouts are real.

## Quick demo

```
1. windows_launch { "app": "calc.exe" }
   → Window handle: w1

2. windows_snapshot { "handle": "w1" }
   → - window "Calculator" [ref=w1]
       - button "Seven" [ref=w1e4]
       - button "Multiply by" [ref=w1e35]
       - text "Display is 0" [ref=w1e15]

3. windows_batch { "actions": [
     { "action": "click", "ref": "w1e4" },
     { "action": "click", "ref": "w1e35" },
     { "action": "click", "ref": "w1e4" },
     { "action": "click", "ref": "w1e38" },
     { "action": "snapshot", "handle": "w1" }
   ] }
   → ... text "Display is 9" ...
```

## Requirements

- Windows 10/11 (x64 or arm64)
- One of:
  - the .NET 10 runtime (for the `dotnet tool` install), or
  - nothing at all (self-contained zip)

## Install

Installing the server and registering it with your MCP client are **two separate steps** — every
option below needs the client entry shown in [Configure your MCP client](#configure-your-mcp-client).

### Option 1 — NuGet.org (recommended)

**Step 1 — install the server command** (once):

```powershell
dotnet tool install -g WinPilot.Mcp
```

This only puts the `winpilot-mcp` command on your PATH; it does not touch any MCP client.

**Step 2 — add the server to your client** (opencode example):

```jsonc
"mcp": {
  "winpilot": {
    "type": "local",
    "command": ["winpilot-mcp"]
  }
}
```

That's it. The other clients (VS Code, Claude Desktop) use the same entry with their own file layout.

> **No install at all:** with the .NET 10 SDK you can skip step 1 entirely — `dnx` runs the published
> package directly. Use `"command": ["dotnet", "dnx", "WinPilot.Mcp"]` instead; it downloads and caches
> the server on first use (~2 s, then instant).

### Option 2 — one-liner installers from this repository

```powershell
irm https://raw.githubusercontent.com/nestebe/WinPilot.Mcp/main/install.ps1 | iex
```

installs or updates the .NET tool from the latest [release](https://github.com/nestebe/WinPilot.Mcp/releases).
For a self-contained executable (no .NET required):

```powershell
irm https://raw.githubusercontent.com/nestebe/WinPilot.Mcp/main/install-exe.ps1 | iex
```

extracts `winpilot-mcp.exe` to `%LOCALAPPDATA%\WinPilot.Mcp` and prints the config snippet.
Both scripts are re-runnable to update — then add the printed entry to your MCP client (step 2 above).

### Option 3 — manual downloads

- **.NET tool:** download `WinPilot.Mcp.<version>.nupkg` from [Releases](https://github.com/nestebe/WinPilot.Mcp/releases)
  and install it from the folder you downloaded it into:
  `dotnet tool install -g WinPilot.Mcp --add-source <folder-with-the-nupkg>`
- **Self-contained executable:** download `WinPilot.Mcp-win-x64-<version>.zip` (or `win-arm64`), extract it
  anywhere stable, and point your client at the exe (escape the backslashes, or use forward slashes):

  ```jsonc
  "mcp": { "winpilot": { "type": "local", "command": ["C:\\Tools\\WinPilot.Mcp\\winpilot-mcp.exe"] } }
  ```

  To update it, close the opencode session (the installer refuses to replace a running exe) and re-run
  the installer, or extract the new zip over the folder.

## Configure your MCP client

### opencode (primary target)

`opencode.jsonc`:

```jsonc
{
  "$schema": "https://opencode.ai/config.json",
  "mcp": {
    "winpilot": {
      "type": "local",
      "command": ["winpilot-mcp"]
    }
  }
}
```

Zero-install variant: `"command": ["dotnet", "dnx", "WinPilot.Mcp"]`.

> opencode applies a 30 s timeout to server startup and tool listing. The first `dnx` run downloads the package — if your machine is slow, raise the timeout: `"timeout": 60000`.

### VS Code / GitHub Copilot

`.vscode/mcp.json` (or your user-level MCP config):

```json
{
  "servers": {
    "winpilot": { "type": "stdio", "command": "winpilot-mcp" }
  }
}
```

### Claude Desktop

`claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "winpilot": { "command": "winpilot-mcp" }
  }
}
```

## Tools

| Tool | What it does |
| --- | --- |
| `windows_launch` | Launch an app (path or UWP app id); returns window handle, title, and tracks the process |
| `windows_snapshot` | Accessibility tree with element refs (the primary tool for understanding a window) |
| `windows_click` | Click by ref; pattern-first (Invoke → Toggle → SelectionItem), mouse fallback |
| `windows_type` | Type into an element (focused first) or the focused element |
| `windows_fill` | Clear and fill a text field (Value pattern first) |
| `windows_send_keys` | One chord (`Ctrl+Right`) or a sequence (`["Ctrl+C", "Down"]`) |
| `windows_get_text` | Text content of an element (value, selection, name, document text) |
| `windows_screenshot` | PNG of screen/window/element; optional background capture and `savePath` |
| `windows_list_windows` | All top-level windows (Win32 fast path; never blocks on UIA) |
| `windows_focus` | Bring a window to front by handle or title substring |
| `windows_close` | Graceful close; `force: true` kills apps launched by this server |
| `windows_batch` | Several actions in one call: click, type, fill, wait, snapshot, sendKeys, getText |
| `windows_wait_for` | Wait until an element exists and is usable; returns a fresh ref |

Element refs (`w1e5`) are regenerated by every snapshot of that window — always act on refs from the latest snapshot. When an older ref can no longer be resolved (the tree shrank, or the element was recreated), WinPilot re-locates it once by fingerprint (automation id first, then name + control type); if that fails you get an actionable `ELEMENT_STALE` error.

## Reliability model

- **The protocol layer never blocks.** UI Automation runs on one dedicated worker thread; pings, cancellation, and window-level tools keep working while a UIA call is busy.
- **Two-level timeouts.** A soft timeout (default 30 s) fails the call with an actionable error; past a hard timeout (default 90 s) the worker is recycled with a fresh UIA context, and old refs report `ELEMENT_STALE`.
- **Win32 fast paths.** `windows_list_windows`, `windows_focus`, `windows_close`, and launch discovery don't use UIA at all.
- **Bounded snapshots.** Depth (10), node count (2000), and a time budget (10 s) prevent giant trees; truncation is marked explicitly.
- **Clean shutdown.** Apps launched by the server are closed on exit (opt out below).

## Configuration

All settings are optional; environment variables use the `WINPILOT_` prefix. An optional `appsettings.json` works too: a `WinPilot` section for the engine settings below, and top-level `LogLevel`/`LogFile` keys for logging.

| Environment variable | Default | Meaning |
| --- | --- | --- |
| `WINPILOT_OPERATION_TIMEOUT_SECONDS` | 30 | Soft timeout per operation |
| `WINPILOT_HARD_TIMEOUT_SECONDS` | 90 | Worker recycle threshold |
| `WINPILOT_SNAPSHOT_MAX_DEPTH` | 10 | Default snapshot depth |
| `WINPILOT_SNAPSHOT_MAX_NODES` | 2000 | Snapshot node budget |
| `WINPILOT_SNAPSHOT_TIME_BUDGET_MS` | 10000 | Snapshot time budget |
| `WINPILOT_LAUNCH_WINDOW_TIMEOUT_MS` | 15000 | Launch → window wait |
| `WINPILOT_CLOSE_WINDOW_TIMEOUT_MS` | 5000 | Graceful close wait |
| `WINPILOT_WAIT_FOR_ELEMENT_TIMEOUT_MS` | 10000 | Default `windows_wait_for` timeout |
| `WINPILOT_KEEP_APPS_ON_EXIT` | false | Don't close apps launched by the server |
| `WINPILOT_LOG_LEVEL` | Warning | Server log level |
| `WINPILOT_LOG_FILE` | — | Also write logs to this file |

## Troubleshooting

- **The client shows the server as disconnected/failed.** Check the client's MCP log first; then enable `WINPILOT_LOG_FILE` and reproduce. The server never writes to stdout except JSON-RPC, and it survives tool errors by design.
- **`OPERATION_TIMEOUT` errors.** A modal dialog or a busy UI Automation provider is blocking the target app. Dismiss it and retry; window-level tools stay available meanwhile.
- **`ELEMENT_STALE`.** The UI changed since the snapshot. Run `windows_snapshot` and use the new refs.
- **`ENGINE_BUSY`.** Another operation is still running past the soft timeout; retry shortly. `windows_batch` can combine several steps to avoid this.

## Safety and limitations

- Keyboard input is focus-dependent: `windows_send_keys` and `windows_type` without a ref go to the active keyboard focus.
- `windows_screenshot` `background: true` requires a window handle and falls back to a normal capture when Windows returns a blank frame.
- `savePath` accepts absolute local `.png` paths only; existing files are not replaced unless `overwrite: true`.
- Works with apps that support UI Automation (Win32, WinForms, WPF, UWP). Electron apps depend on their accessibility support; games typically don't work.
- Apps running **as administrator** (elevated) cannot be automated from a non-elevated server: Windows blocks UI Automation across integrity levels (UIPI). WinPilot detects this and returns an actionable error — run your MCP client (and therefore the server) elevated to control administrator apps.
- Integration tests require an interactive desktop session and skip themselves otherwise.

## Building from source

```powershell
dotnet build WinPilot.Mcp.slnx
dotnet test tests/WinPilot.Automation.Tests
dotnet test tests/WinPilot.Mcp.Tests
dotnet test tests/WinPilot.IntegrationTests   # interactive desktop required
dotnet pack src/WinPilot.Mcp -c Release -o artifacts
```

See [docs/architecture.md](docs/architecture.md) for the engine design.

## License

MIT — see [LICENSE](LICENSE). WinPilot.Mcp is built on the [FlaUI](https://github.com/FlaUI/FlaUI) library (MIT).
