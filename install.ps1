<#
.SYNOPSIS
  Build (or use the prebuilt bundle), deploy and register the Civil 3D MCP bridge.

.DESCRIPTION
  Run from the repo root in a normal (non-admin) PowerShell:
      powershell -ExecutionPolicy Bypass -File .\install.ps1

  1. Detects installed Civil 3D releases (2025, 2026, 2027) under Program Files.
  2. Builds one DLL per release with the .NET SDK (2027 = net10.0-windows,
     2025/2026 = net8.0-windows). If no .NET 10 SDK is found, or -Prebuilt is
     passed, it uses dist\Civil3DMcpBridge-<ver>.bundle.zip instead.
  3. Deploys a multi-release bundle to
     %APPDATA%\Autodesk\ApplicationPlugins\Civil3DMcpBridge.bundle
     (a trusted location, so SECURELOAD=1 accepts it; NETLOAD is not needed).
  4. Creates a private Python venv at %LOCALAPPDATA%\Civil3DMcpBridge\venv and
     installs the MCP server into it.
  5. Registers "civil3d" in Claude Desktop's config (backup kept). No fixed URL:
     the server discovers every running Civil 3D session automatically.

  Close Civil 3D before running (loaded DLLs are locked).

.PARAMETER Releases   Restrict to specific years, e.g. -Releases 2027
.PARAMETER Prebuilt   Skip compiling; deploy the bundled prebuilt DLLs
.PARAMETER Net10For2026  Build 2026 against .NET 10 (only if your 2026 has the Aug-2026 .NET 10 update)
.PARAMETER SkipPython Plugin only
.PARAMETER PythonExe  Python 3.11+ launcher (default: py -3, then python)
#>
[CmdletBinding()]
param(
    [int[]]$Releases,
    [switch]$Prebuilt,
    [switch]$Net10For2026,
    [switch]$SkipPython,
    [string]$PythonExe
)

$ErrorActionPreference = "Stop"
$Version = "0.4.0"
$root = Split-Path -Parent $MyInvocation.MyCommand.Definition
Set-Location $root

function Step($m) { Write-Host ""; Write-Host "==> $m" -ForegroundColor Cyan }
function Ok($m)   { Write-Host "    [OK] $m" -ForegroundColor Green }
function Warn($m) { Write-Host "    [WARN] $m" -ForegroundColor Yellow }
function Fail($m) { Write-Host "    [FAIL] $m" -ForegroundColor Red; exit 1 }

# ---------------------------------------------------------------------------
Step "1/6 Detecting Civil 3D releases"
# ---------------------------------------------------------------------------
$found = @()
foreach ($y in 2025, 2026, 2027) {
    $p = "C:\Program Files\Autodesk\AutoCAD $y"
    if (Test-Path (Join-Path $p "C3D\AeccDbMgd.dll")) { $found += [pscustomobject]@{ Year = $y; Path = $p } }
}
if ($Releases) { $found = $found | Where-Object { $Releases -contains $_.Year } }
if (-not $found) { Fail "No Civil 3D 2025/2026/2027 install found under C:\Program Files\Autodesk" }
$found | ForEach-Object { Ok "Civil 3D $($_.Year) at $($_.Path)" }

if (Get-Process acad -ErrorAction SilentlyContinue) {
    Warn "Civil 3D (acad.exe) is running. Close it first or the DLL copy will fail."
    $ans = Read-Host "    Continue anyway? (y/N)"
    if ($ans -ne "y") { exit 1 }
}

# ---------------------------------------------------------------------------
Step "2/6 Building add-in"
# ---------------------------------------------------------------------------
$stage = Join-Path $root "plugin\bin\bundle-stage"
if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
New-Item -ItemType Directory -Force -Path $stage | Out-Null

$sdkOk = $false
if (-not $Prebuilt) {
    try { $sdks = & dotnet --list-sdks 2>$null; $sdkOk = [bool]($sdks | Where-Object { $_ -match '^10\.' }) } catch { }
    if (-not $sdkOk) { Warn ".NET 10 SDK not found (winget install Microsoft.DotNet.SDK.10). Falling back to prebuilt bundle." }
}

$built = @()
if ($sdkOk -and -not $Prebuilt) {
    foreach ($r in $found) {
        $bargs = @("build", "plugin\Civil3DMcpBridge.csproj", "-c", "Release", "-p:C3DVersion=$($r.Year)", "-p:Civil3DPath=$($r.Path)", "-nologo", "-v", "q")
        if ($r.Year -eq 2026 -and $Net10For2026) { $bargs += "-p:C3DNet=net10.0-windows" }
        & dotnet @bargs
        if ($LASTEXITCODE -ne 0) { Fail "build failed for Civil 3D $($r.Year)" }
        $dst = Join-Path $stage $r.Year
        New-Item -ItemType Directory -Force -Path $dst | Out-Null
        Copy-Item "plugin\bin\Release\$($r.Year)\Civil3DMcpBridge.*" $dst
        $built += $r.Year
        Ok "built for $($r.Year)"
    }
} else {
    $zip = Join-Path $root "dist\Civil3DMcpBridge-$Version.bundle.zip"
    if (-not (Test-Path $zip)) { Fail "prebuilt bundle missing: $zip" }
    $tmp = Join-Path $env:TEMP "c3dmcp-prebuilt"
    if (Test-Path $tmp) { Remove-Item -Recurse -Force $tmp }
    Expand-Archive -Path $zip -DestinationPath $tmp
    foreach ($r in $found) {
        $src = Join-Path $tmp "Civil3DMcpBridge.bundle\Contents\$($r.Year)"
        if (Test-Path $src) {
            Copy-Item -Recurse $src (Join-Path $stage $r.Year)
            $built += $r.Year
            Ok "prebuilt DLL for $($r.Year)"
        } else { Warn "no prebuilt DLL for $($r.Year)" }
    }
}
if (-not $built) { Fail "nothing to deploy" }

# ---------------------------------------------------------------------------
Step "3/6 Deploying bundle"
# ---------------------------------------------------------------------------
$bundle = Join-Path $env:APPDATA "Autodesk\ApplicationPlugins\Civil3DMcpBridge.bundle"
$contents = Join-Path $bundle "Contents"
New-Item -ItemType Directory -Force -Path $contents | Out-Null

# Remove the v0.3 single-DLL layout so it can't load alongside the new one.
Get-ChildItem $contents -File -ErrorAction SilentlyContinue | Where-Object { $_.Name -like "Civil3DMcpBridge*" } | ForEach-Object {
    try { Remove-Item -Force $_.FullName } catch { Fail "$($_.FullName) is locked. Close Civil 3D and rerun." }
}

foreach ($y in $built) {
    $dst = Join-Path $contents $y
    New-Item -ItemType Directory -Force -Path $dst | Out-Null
    try { Copy-Item -Force (Join-Path $stage "$y\*") $dst } catch { Fail "copy to $dst failed (Civil 3D $y still running?)" }
}
$manual = Join-Path $root "docs\USER_MANUAL.pdf"
if (Test-Path $manual) { $built | ForEach-Object { Copy-Item -Force $manual (Join-Path $contents "$_\USER_MANUAL.pdf") } }

# PackageContents.xml listing only what was deployed.
$series = @{ 2025 = "R25.0"; 2026 = "R25.1"; 2027 = "R26.0" }
$comp = ($built | Sort-Object | ForEach-Object {
@"
  <Components Description="Civil 3D $_">
    <RuntimeRequirements OS="Win64" Platform="AutoCAD*" SeriesMin="$($series[$_])" SeriesMax="$($series[$_])" />
    <ComponentEntry AppName="Civil3DMcpBridge" ModuleName="./Contents/$_/Civil3DMcpBridge.dll"
                    AppDescription="MCP bridge for Civil 3D $_" LoadOnAutoCADStartup="True" LoadOnCommandInvocation="False" />
  </Components>
"@ }) -join "`n"
@"
<?xml version="1.0" encoding="utf-8"?>
<ApplicationPackage SchemaVersion="1.0" AutodeskProduct="AutoCAD" ProductType="Application"
                    Name="Civil3DMcpBridge" Description="Bridges Claude (MCP) to Civil 3D over local HTTP"
                    AppVersion="$Version" Author="AfroBIM Studios (Venture Kubariho)"
                    ProductCode="{C3DA0000-0000-0000-0000-000000000001}" UpgradeCode="{C3DA0000-0000-0000-0000-000000000002}">
  <CompanyDetails Name="AfroBIM Studios" Url="https://afrobim.com" />
$comp
</ApplicationPackage>
"@ | Set-Content -Encoding UTF8 (Join-Path $bundle "PackageContents.xml")

Get-ChildItem -Recurse $bundle | Unblock-File -ErrorAction SilentlyContinue
Ok "bundle at $bundle (releases: $($built -join ', '))"

if ($SkipPython) { Warn "Python steps skipped (-SkipPython)"; exit 0 }

# ---------------------------------------------------------------------------
Step "4/6 Python environment"
# ---------------------------------------------------------------------------
$py = $null
$cands = @()
if ($PythonExe) { $cands += ,@($PythonExe) } else { $cands += ,@("py", "-3"); $cands += ,@("python") }
foreach ($c in $cands) {
    try {
        $v = & $c[0] @($c | Select-Object -Skip 1) -c "import sys;print('%d.%d'%sys.version_info[:2])" 2>$null
        if ($v -and [version]$v -ge [version]"3.11") { $py = $c; Ok "Python $v ($($c -join ' '))"; break }
    } catch { }
}
if (-not $py) { Fail "Python 3.11+ not found. Install with: winget install Python.Python.3.12" }

$venv = Join-Path $env:LOCALAPPDATA "Civil3DMcpBridge\venv"
if (-not (Test-Path (Join-Path $venv "Scripts\python.exe"))) {
    & $py[0] @($py | Select-Object -Skip 1) -m venv $venv
    if ($LASTEXITCODE -ne 0) { Fail "venv creation failed" }
}
$vpy = Join-Path $venv "Scripts\python.exe"
& $vpy -m pip install --upgrade pip -q
& $vpy -m pip install -q -e (Join-Path $root "server")
if ($LASTEXITCODE -ne 0) { Fail "pip install failed" }
$exe = Join-Path $venv "Scripts\civil3d-mcp.exe"
Ok "MCP server installed: $exe"

# ---------------------------------------------------------------------------
Step "5/6 Registering with Claude Desktop"
# ---------------------------------------------------------------------------
$cfgPaths = @(
    (Join-Path $env:APPDATA "Claude\claude_desktop_config.json"),
    (Join-Path $env:LOCALAPPDATA "Packages\Claude_pzs8sxrjxfjjc\LocalCache\Roaming\Claude\claude_desktop_config.json")
) | Where-Object { Test-Path (Split-Path $_) }
if (-not $cfgPaths) { $cfgPaths = @(Join-Path $env:APPDATA "Claude\claude_desktop_config.json") }

foreach ($cfg in $cfgPaths) {
    New-Item -ItemType Directory -Force -Path (Split-Path $cfg) | Out-Null
    if (Test-Path $cfg) {
        Copy-Item $cfg "$cfg.bak-$(Get-Date -Format yyyyMMddHHmmss)"
        $json = Get-Content $cfg -Raw | ConvertFrom-Json
    } else { $json = [pscustomobject]@{} }
    if (-not ($json.PSObject.Properties.Name -contains "mcpServers")) {
        $json | Add-Member -NotePropertyName mcpServers -NotePropertyValue ([pscustomobject]@{})
    }
    $entry = [pscustomobject]@{ command = $exe; args = @(); env = [pscustomobject]@{ CIVIL3D_TIMEOUT_S = "300" } }
    if ($json.mcpServers.PSObject.Properties.Name -contains "civil3d") { $json.mcpServers.PSObject.Properties.Remove("civil3d") }
    $json.mcpServers | Add-Member -NotePropertyName civil3d -NotePropertyValue $entry
    $json | ConvertTo-Json -Depth 20 | Set-Content -Encoding UTF8 $cfg
    Ok "updated $cfg (backup saved)"
}

# ---------------------------------------------------------------------------
Step "6/6 Done"
# ---------------------------------------------------------------------------
$ports = ($built | ForEach-Object { "$_ -> 78$($_ % 100)" }) -join ", "
Write-Host @"

    1. Start Civil 3D and open a drawing. The command line shows
       "[Civil3DMcpBridge] Civil 3D <year> listening on http://127.0.0.1:78xx".
       Default ports: $ports. Run MCPSTATUS any time to check.
    2. Restart Claude Desktop (quit from the tray). The 'civil3d' server lists ~128 tools.
    3. Ask Claude: "Run civil3d_list_instances, then the bridge health check."

    Test without Civil 3D:  $vpy tests\mock_bridge.py   (then point CIVIL3D_BRIDGE_URL at it)
"@
