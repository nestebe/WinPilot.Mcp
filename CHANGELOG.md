# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses
[Semantic Versioning](https://semver.org/).

## [Unreleased]

## [0.1.2] - 2026-09-29

### Fixed

- When a packaged app or launcher stub creates its window in a different process, that process
  is now tracked as owned too: `windows_close` with `force: true` and shutdown cleanup can
  close the real app (for example Windows 11 Notepad launched from `notepad.exe`).

## [0.1.1] - 2026-09-29

### Fixed

- Launch discovery now falls back to baseline window diffing when no window appears for the
  launched process id — packaged apps and launcher stubs (for example Windows 11 Notepad) that
  create their window in another process are found reliably.
- Default launch window timeout raised from 10 s to 15 s: packaged-app cold starts can exceed
  the old default on first launch.

## [0.1.0] - 2026-09-28

Initial release: an MCP server for Windows desktop automation with accessibility snapshots and element refs.

### Added

- `windows_launch`, `windows_snapshot`, `windows_click`, `windows_type`, `windows_fill`,
  `windows_send_keys`, `windows_get_text`, `windows_screenshot`, `windows_list_windows`,
  `windows_focus`, `windows_close`, `windows_batch` — the full window and element tool set.
- `windows_wait_for`: waits until an element exists, is enabled, and is on-screen.
- `force: true` on `windows_close` for apps launched by the server.
- `sendKeys` and `getText` batch actions.
- Automatic one-shot re-resolution of stale element refs by fingerprint.
- Snapshot guard rails with explicit truncation markers (depth, node count, time budget).

### Reliability

- All UI Automation work is serialized on a dedicated worker thread; the MCP protocol
  layer always answers pings and cancellation.
- Two-level timeouts: soft (caller error) and hard (worker recycling with fresh UIA context).
- Win32 fast paths for window listing, focus, close, and launch discovery.
- Official ModelContextProtocol C# SDK (stdio transport, attribute-based tools, DI).
- Structured logging on stderr only; optional file logging via `WINPILOT_LOG_FILE`.

### Changed

- Solution layout: `WinPilot.Automation` (engine) + `WinPilot.Mcp` (MCP host), .NET 10.
- Install options: `dotnet tool install -g WinPilot.Mcp`, `dotnet dnx WinPilot.Mcp`,
  or self-contained zips.
