<#
.SYNOPSIS
  Build, deploy, and register the civil3d-mcp scaffold.

.DESCRIPTION
  One-shot installer for Windows. Run from the repo root:
      powershell -ExecutionPolicy Bypass -File .\install.ps1

  Steps:
    1. Verify prerequisites: Civil 3D path, .NET 8 SDK, Python 3.11+.
    2. dotnet build -c Release on plugin/.
    3. Deploy the bundle to %APPDATA%\Autodesk\ApplicationPlugins\Civil3DMcpBridge.bundle.
    4. pip install -e server\ (creates the `civil3d-mcp` console script).
    5. Merge entry into %APPDATA%\Claude\claude_desktop_config.json.
    6. Smoke-test by invoking the Python server's --help equivalent
       (FastMCP doesn't expose --help, but we verify the entry point is on PATH).

  Civil 3D itself must be running with the add-in loaded for the /health
  endpoint to respond — that's a manual step at the end.

.PARAMETER Civil3DPath
  Override the Civil 3D install path (default: C:\Program Files\Autodesk\AutoCAD 2026).

.PARAMETER PerMachine
  Install the bundle to C:\ProgramData instead of %APPDATA% (needs admin).

.PARAMETER SkipBuild
  Skip the dotnet build step (use a previously-built DLL).

.PARAMETER SkipPython
  Skip pip install + Claude config merge (plugin-only deployment).

.PARAMETER PythonExe
  Override the Python interpreter (default: `python` from PATH).
#>

[CmdletBinding()]
param(
    [string]$Civil3DPath = "C:\Program Files\Autodesk\AutoCAD 2026",
    [switch]$PerMachine,
    [switch]$SkipBuild,
    [switch]$SkipPython,
    [string]$PythonExe = "python"
)

$ErrorActionPreference = "Stop"
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Definition
Push-Location $scriptRoot

function Write-Step($msg) {
    Write-Host ""
    Write-Host "==> $msg" -ForegroundColor Cyan
}

function Write-Ok($msg)   { Write-Host "    [OK] $msg"   -ForegroundColor Green }
function Write-Warn($msg) { Write-Host "    [WARN] $msg" -ForegroundColor Yellow }
function Write-Err($msg)  { Write-Host "    [FAIL] $msg" -ForegroundColor Red }

try {
    # =====================================================================
    Write-Step "1/6 — Checking prerequisites"
    # =====================================================================

    if (-not (Test-Path $Civil3DPath)) {
        Write-Err "Civil 3D path does not exist: $Civil3DPath"
        Write-Err "Pass -Civil3DPath '<your path>' to override."
        exit 1
    }
    Write-Ok "Civil 3D found at $Civil3DPath"

    $aeccDll = Join-Path $Civil3DPath "C3D\AeccDbMgd.dll"
    if (-not (Test-Path $aeccDll)) {
        Write-Err "Civil 3D managed API DLL not found at $aeccDll"
        Write-Err "Is this a plain AutoCAD install (no Civil 3D vertical)?"
        exit 1
    }
    Write-Ok "Civil 3D managed APIs present (AeccDbMgd.dll)"

    if (-not $SkipBuild) {
        try {
            $dotnetVersion = & dotnet --version
            Write-Ok ".NET SDK $dotnetVersion"
        } catch {
            Write-Err ".NET 8 SDK not found on PATH. Install: winget install Microsoft.DotNet.SDK.8"
            exit 1
        }
    }

    if (-not $SkipPython) {
        try {
            $pyVersion = & $PythonExe --version 2>&1
            Write-Ok "Python: $pyVersion"
        } catch {
            Write-Err "Python not found via '$PythonExe'. Install Python 3.11+ or pass -PythonExe."
            exit 1
        }
    }

    # =====================================================================
    Write-Step "2/6 — Building the C# add-in"
    # =====================================================================
    if ($SkipBuild) {
        Write-Warn "Skipped (-SkipBuild)"
    } else {
        Push-Location plugin
        try {
            & dotnet build -c Release -p:Civil3DPath="$Civil3DPath"
            if ($LASTEXITCODE -ne 0) {
                Write-Err "dotnet build failed (exit code $LASTEXITCODE)"
                exit 1
            }
        } finally {
            Pop-Location
        }
        Write-Ok "Build succeeded"
    }

    $buildOutput = Join-Path $scriptRoot "plugin\bin\Release\net8.0-windows"
    $dll = Join-Path $buildOutput "Civil3DMcpBridge.dll"
    if (-not (Test-Path $dll)) {
        Write-Err "Expected build output not found: $dll"
        exit 1
    }

    # =====================================================================
    Write-Step "3/6 — Deploying bundle"
    # =====================================================================
    $bundleParent = if ($PerMachine) {
        "$env:ProgramData\Autodesk\ApplicationPlugins"
    } else {
        "$env:APPDATA\Autodesk\ApplicationPlugins"
    }
    $bundle = Join-Path $bundleParent "Civil3DMcpBridge.bundle"
    $contents = Join-Path $bundle "Contents"

    if (Test-Path $bundle) {
        Write-Warn "Existing bundle at $bundle — overwriting"
    }
    New-Item -ItemType Directory -Force -Path $contents | Out-Null

    Copy-Item -Force `
        (Join-Path $scriptRoot "plugin\PackageContents.xml") `
        (Join-Path $bundle "PackageContents.xml")

    Get-ChildItem -Path $buildOutput -File | ForEach-Object {
        # Skip the host DLLs that Civil 3D already provides — leaving them
        # in the bundle is harmless but uses extra disk and risks version drift.
        if ($_.Name -in @(
            "AcCoreMgd.dll", "AcDbMgd.dll", "AcMgd.dll",
            "AeccDbMgd.dll", "AeccPressurePipesMgd.dll"
        )) { return }
        Copy-Item -Force $_.FullName $contents
    }
    Write-Ok "Bundle deployed to $bundle"

    # =====================================================================
    Write-Step "4/6 — Installing Python MCP server"
    # =====================================================================
    if ($SkipPython) {
        Write-Warn "Skipped (-SkipPython)"
    } else {
        $serverDir = Join-Path $scriptRoot "server"
        Push-Location $serverDir
        try {
            & $PythonExe -m pip install -e . 2>&1 | ForEach-Object { Write-Host "    $_" }
            if ($LASTEXITCODE -ne 0) {
                Write-Err "pip install failed (exit code $LASTEXITCODE)"
                exit 1
            }
        } finally {
            Pop-Location
        }
        Write-Ok "Python package installed (civil3d-mcp entry point on PATH)"
    }

    # =====================================================================
    Write-Step "5/6 — Registering with Claude Desktop"
    # =====================================================================
    if ($SkipPython) {
        Write-Warn "Skipped (-SkipPython)"
    } else {
        $claudeConfig = Join-Path $env:APPDATA "Claude\claude_desktop_config.json"
        $claudeDir = Split-Path -Parent $claudeConfig
        if (-not (Test-Path $claudeDir)) {
            New-Item -ItemType Directory -Force -Path $claudeDir | Out-Null
        }

        if (Test-Path $claudeConfig) {
            Write-Ok "Existing Claude config found — merging entry"
            $existing = Get-Content $claudeConfig -Raw | ConvertFrom-Json
        } else {
            Write-Ok "No existing Claude config — creating new one"
            $existing = [PSCustomObject]@{}
        }

        # Ensure mcpServers property exists.
        if (-not ($existing.PSObject.Properties.Name -contains "mcpServers")) {
            $existing | Add-Member -NotePropertyName "mcpServers" -NotePropertyValue ([PSCustomObject]@{})
        }

        # Resolve the absolute path to civil3d-mcp.exe (or fall back to python -m).
        $civil3dMcpCmd = (Get-Command civil3d-mcp -ErrorAction SilentlyContinue).Path
        if ($civil3dMcpCmd) {
            $entry = [PSCustomObject]@{
                command = $civil3dMcpCmd
                env = [PSCustomObject]@{
                    CIVIL3D_BRIDGE_URL = "http://127.0.0.1:7800"
                }
            }
        } else {
            Write-Warn "civil3d-mcp not on PATH — using 'python -m' fallback"
            $entry = [PSCustomObject]@{
                command = (Get-Command $PythonExe).Path
                args = @("-m", "civil3d_mcp.server")
                env = [PSCustomObject]@{
                    CIVIL3D_BRIDGE_URL = "http://127.0.0.1:7800"
                }
            }
        }

        # Add / replace.
        if ($existing.mcpServers.PSObject.Properties.Name -contains "civil3d") {
            $existing.mcpServers.PSObject.Properties.Remove("civil3d")
        }
        $existing.mcpServers | Add-Member -NotePropertyName "civil3d" -NotePropertyValue $entry

        $existing | ConvertTo-Json -Depth 10 | Set-Content -Path $claudeConfig -Encoding UTF8
        Write-Ok "Claude config updated at $claudeConfig"
    }

    # =====================================================================
    Write-Step "6/6 — Smoke test"
    # =====================================================================
    Write-Host ""
    Write-Host "    The bridge can only respond when Civil 3D is running. Open Civil 3D, open a DWG,"
    Write-Host "    then run this from a new PowerShell window:"
    Write-Host ""
    Write-Host "        curl http://127.0.0.1:7800/health" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "    Expected: {""ok"":true, ""version"":""0.2.0"", ""toolCount"":N, ""tools"":[...]}"
    Write-Host ""
    Write-Host "    To test the Python MCP layer without Civil 3D, run the mock bridge:"
    Write-Host ""
    Write-Host "        python tests\mock_bridge.py" -ForegroundColor Yellow
    Write-Host "        python tests\integration_test.py --against-mock" -ForegroundColor Yellow
    Write-Host ""

    Write-Host "==> Install complete." -ForegroundColor Green
    Write-Host ""
    Write-Host "Next steps:"
    Write-Host "  1. Start Civil 3D, open any DWG."
    Write-Host "  2. At the C3D command line, type MCPSTATUS — confirm 'listening on http://127.0.0.1:7800'."
    Write-Host "  3. Restart Claude Desktop. Look for the hammer icon → 'civil3d' with ~66 tools."
    Write-Host "  4. Ask Claude: 'Run the civil3d bridge health check.'"

} finally {
    Pop-Location
}
