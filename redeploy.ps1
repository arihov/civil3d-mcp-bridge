<#
.SYNOPSIS
  Redeploy the freshly-built plugin DLL into the Civil 3D bundle.

.DESCRIPTION
  Civil 3D locks the loaded DLL while running. Close all Civil 3D
  windows first, then run:
      powershell -ExecutionPolicy Bypass -File .\redeploy.ps1

  The script also copies USER_MANUAL.pdf beside the DLL so the
  MCPMANUAL command finds it.
#>
[CmdletBinding()]
param(
    [string]$BundleParent = "$env:APPDATA\Autodesk\ApplicationPlugins"
)

$ErrorActionPreference = "Stop"
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Definition

$build = Join-Path $scriptRoot "plugin\bin\Release\net8.0-windows"
if (-not (Test-Path "$build\Civil3DMcpBridge.dll")) {
    Write-Error "Build output not found at $build. Run 'dotnet build -c Release' in plugin/ first."
    exit 1
}

$bundle = Join-Path $BundleParent "Civil3DMcpBridge.bundle"
$contents = Join-Path $bundle "Contents"
if (-not (Test-Path $contents)) {
    New-Item -ItemType Directory -Force -Path $contents | Out-Null
    Copy-Item -Force (Join-Path $scriptRoot "plugin\PackageContents.xml") (Join-Path $bundle "PackageContents.xml")
}

# Check the existing DLL is not locked (Civil 3D running with old version
# loaded). If it is, refuse rather than producing a confusing error.
$dst = Join-Path $contents "Civil3DMcpBridge.dll"
if (Test-Path $dst) {
    try {
        $fs = [System.IO.File]::Open($dst, 'Open', 'Write')
        $fs.Close()
    } catch {
        Write-Host ""
        Write-Host "ERROR: $dst is locked by another process." -ForegroundColor Red
        Write-Host "Close all Civil 3D windows first, then run this script again." -ForegroundColor Yellow
        exit 1
    }
}

Copy-Item -Force "$build\Civil3DMcpBridge.dll" $dst
Copy-Item -Force "$build\Civil3DMcpBridge.pdb" (Join-Path $contents "Civil3DMcpBridge.pdb")
Copy-Item -Force "$build\Civil3DMcpBridge.deps.json" (Join-Path $contents "Civil3DMcpBridge.deps.json")

$pdf = Join-Path $scriptRoot "docs\USER_MANUAL.pdf"
if (Test-Path $pdf) {
    Copy-Item -Force $pdf (Join-Path $contents "USER_MANUAL.pdf")
}

Write-Host "==> Plugin redeployed to $bundle" -ForegroundColor Green
Write-Host "    Start Civil 3D, run MCPSTATUS, then MCPSHOW to open the palette." -ForegroundColor Cyan
