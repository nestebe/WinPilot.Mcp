# Contributing

Thanks for your interest in WinPilot.Mcp.

## Build and test

```powershell
dotnet build WinPilot.Mcp.slnx -c Release
dotnet test tests/WinPilot.Automation.Tests -c Release
dotnet test tests/WinPilot.Mcp.Tests -c Release
dotnet test tests/WinPilot.IntegrationTests -c Release   # needs an interactive desktop
dotnet format WinPilot.Mcp.slnx --verify-no-changes
```

The integration tests launch a real WinForms fixture app (`tests/TestApps`) and skip
themselves automatically when no interactive desktop session is available.

## Guidelines

- Target framework and analyzers are configured centrally (`Directory.Build.props`);
  the build treats warnings as errors, and `dotnet format` is enforced in CI.
- Comments and identifiers are in English.
- Follow test-driven development: write the failing test first, then the minimal change.
- Keep the dependency direction: `WinPilot.Mcp` → `WinPilot.Automation`, never the reverse.
- Never write to stdout from `src/` — stdout carries JSON-RPC only; use `ILogger` (stderr).
- The agent-facing contract (tool names, parameters, snapshot text, ref format) is covered
  by tests and a committed schema snapshot (`tests/WinPilot.IntegrationTests/Protocol/tool-schemas.snap.json`);
  update the snapshot deliberately and explain why in the pull request.

## Pull requests

- One logical change per pull request, with a short rationale.
- Include tests for behavior changes; keep commits focused.
