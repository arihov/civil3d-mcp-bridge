<#
.SYNOPSIS
  Create the GitHub repo and push this branch.

.DESCRIPTION
  Run after `gh auth login` (one-time, opens a browser for OAuth):
      & 'C:\Program Files\GitHub CLI\gh.exe' auth login
  Then:
      powershell -ExecutionPolicy Bypass -File .\publish.ps1
#>
[CmdletBinding()]
param(
    [string]$Name = "civil3d-mcp-bridge",
    [ValidateSet("private", "public")]
    [string]$Visibility = "private",
    [string]$Description = "Civil 3D 2025/2026 MCP bridge — drive Civil 3D in natural language via Claude or any MCP client."
)

$ErrorActionPreference = "Stop"

$gh = (Get-Command gh -ErrorAction SilentlyContinue).Path
if (-not $gh) { $gh = "C:\Program Files\GitHub CLI\gh.exe" }
if (-not (Test-Path $gh)) {
    Write-Error "GitHub CLI not found. Install with: winget install GitHub.cli"
    exit 1
}

& $gh auth status 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Host "Not logged in to GitHub. Run this first:" -ForegroundColor Yellow
    Write-Host "    & '$gh' auth login"
    exit 1
}

Set-Location $PSScriptRoot

Write-Host "==> Creating GitHub repo '$Name' (visibility: $Visibility)" -ForegroundColor Cyan
& $gh repo create $Name --$Visibility --description $Description --source . --push

if ($LASTEXITCODE -eq 0) {
    Write-Host "==> Repo created + initial branch pushed." -ForegroundColor Green
    & $gh repo view --web
} else {
    Write-Error "gh repo create failed (exit $LASTEXITCODE)"
    exit $LASTEXITCODE
}
