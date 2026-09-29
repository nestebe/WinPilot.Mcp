# WinPilot.Mcp installer — self-contained executable
#
# One-liner usage (downloads the latest GitHub release and extracts the self-contained exe):
#   irm https://raw.githubusercontent.com/nestebe/WinPilot.Mcp/main/install-exe.ps1 | iex
#
# Options:
#   -InstallDir <dir>   Where to extract the exe (default: %LOCALAPPDATA%\WinPilot.Mcp).
#   -Version <tag>      Install a specific release tag (for example v0.1.3). Defaults to latest.

[CmdletBinding()]
param(
    [string]$InstallDir,
    [string]$Version
)

$ErrorActionPreference = 'Stop'
$repo = 'nestebe/WinPilot.Mcp'

function Write-Step([string]$message) { Write-Host "WinPilot.Mcp: $message" }

if (-not $InstallDir) {
    $InstallDir = Join-Path $env:LOCALAPPDATA 'WinPilot.Mcp'
}

$rid = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'win-arm64' } else { 'win-x64' }

Write-Step 'locating the latest release...'
if ($Version) {
    $release = Invoke-RestMethod "https://api.github.com/repos/$repo/releases/tags/$Version"
} else {
    $release = Invoke-RestMethod "https://api.github.com/repos/$repo/releases/latest"
}

$asset = $release.assets | Where-Object { $_.name -like "*$rid*.zip" } | Select-Object -First 1
if (-not $asset) {
    throw "No $rid zip asset found in release $($release.tag_name)."
}

$target = $InstallDir.TrimEnd('\')
$exePath = Join-Path $target 'winpilot-mcp.exe'
$runningFromTarget = Get-Process -Name 'winpilot-mcp' -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -eq $exePath }
if ($runningFromTarget) {
    throw "winpilot-mcp.exe from $target is running (used by an opencode session?). Close it first, then re-run this installer to update."
}

$zipPath = Join-Path $env:TEMP $asset.name
Write-Step "downloading $($asset.name)..."
Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $zipPath -UseBasicParsing

Write-Step "extracting to $target..."
New-Item -ItemType Directory -Path $target -Force | Out-Null
Expand-Archive -Path $zipPath -DestinationPath $target -Force
Remove-Item -Force $zipPath -ErrorAction SilentlyContinue

if (-not (Test-Path $exePath)) {
    throw "Extraction failed: $exePath not found."
}

$jsonPath = $exePath -replace '\\', '\\'
Write-Step "installed $($release.tag_name). Configure your MCP client with:"
Write-Host ''
Write-Host '  "mcp": {'
Write-Host '    "winpilot": {'
Write-Host '      "type": "local",'
Write-Host "      `"command`": [`"$jsonPath`"]"
Write-Host '    }'
Write-Host '  }'
