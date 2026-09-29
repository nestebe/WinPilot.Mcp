# WinPilot.Mcp installer
#
# One-liner usage (downloads the latest GitHub release and installs the .NET tool):
#   irm https://raw.githubusercontent.com/nestebe/WinPilot.Mcp/main/install.ps1 | iex
#
# Options:
#   -ToolPath <dir>   Install into a custom directory instead of the global tools folder.
#   -Version <tag>    Install a specific release tag (for example v0.1.3). Defaults to latest.

[CmdletBinding()]
param(
    [string]$ToolPath,
    [string]$Version
)

$ErrorActionPreference = 'Stop'
$repo = 'nestebe/WinPilot.Mcp'

function Write-Step([string]$message) { Write-Host "WinPilot.Mcp: $message" }

Write-Step 'locating the latest release...'
if ($Version) {
    $release = Invoke-RestMethod "https://api.github.com/repos/$repo/releases/tags/$Version"
} else {
    $release = Invoke-RestMethod "https://api.github.com/repos/$repo/releases/latest"
}

$asset = $release.assets | Where-Object { $_.name -like '*.nupkg' } | Select-Object -First 1
if (-not $asset) {
    throw "No .nupkg asset found in release $($release.tag_name)."
}

$version = $release.tag_name.TrimStart('v')
$downloadDir = Join-Path $env:TEMP "winpilot-install-$version"
New-Item -ItemType Directory -Path $downloadDir -Force | Out-Null
$nupkgPath = Join-Path $downloadDir $asset.name

Write-Step "downloading $($asset.name)..."
Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $nupkgPath -UseBasicParsing

$toolPathArgs = if ($ToolPath) { @('--tool-path', $ToolPath) } else { @('--global') }

Write-Step "installing WinPilot.Mcp $version..."
$installed = & dotnet tool list @toolPathArgs 2>$null | Select-String -Pattern 'winpilot' -Quiet
if ($installed) {
    & dotnet tool update @toolPathArgs WinPilot.Mcp --add-source $downloadDir --version $version
} else {
    & dotnet tool install @toolPathArgs WinPilot.Mcp --add-source $downloadDir --version $version
}

if ($LASTEXITCODE -ne 0) {
    throw "Tool installation failed (exit code $LASTEXITCODE)."
}

Remove-Item -Recurse -Force $downloadDir -ErrorAction SilentlyContinue

Write-Step "installed $version. Configure your MCP client with:"
Write-Host ''
Write-Host '  "mcp": {'
Write-Host '    "winpilot": {'
Write-Host '      "type": "local",'
Write-Host '      "command": ["winpilot-mcp"]'
Write-Host '    }'
Write-Host '  }'
