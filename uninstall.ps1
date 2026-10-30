<#
.SYNOPSIS
  Remove the Civil 3D MCP bridge bundle, its Python venv and the Claude Desktop entry.
  Close Civil 3D first. Claude config is backed up before editing.
#>
[CmdletBinding()]
param([switch]$KeepPython)

$ErrorActionPreference = "Stop"
$bundle = Join-Path $env:APPDATA "Autodesk\ApplicationPlugins\Civil3DMcpBridge.bundle"
if (Test-Path $bundle) { Remove-Item -Recurse -Force $bundle; Write-Host "[OK] removed $bundle" -ForegroundColor Green }

$state = Join-Path $env:LOCALAPPDATA "Civil3DMcpBridge"
if ((Test-Path $state) -and -not $KeepPython) { Remove-Item -Recurse -Force $state; Write-Host "[OK] removed $state" -ForegroundColor Green }

foreach ($cfg in @(
    (Join-Path $env:APPDATA "Claude\claude_desktop_config.json"),
    (Join-Path $env:LOCALAPPDATA "Packages\Claude_pzs8sxrjxfjjc\LocalCache\Roaming\Claude\claude_desktop_config.json"))) {
    if (-not (Test-Path $cfg)) { continue }
    Copy-Item $cfg "$cfg.bak-$(Get-Date -Format yyyyMMddHHmmss)"
    $json = Get-Content $cfg -Raw | ConvertFrom-Json
    if ($json.mcpServers -and ($json.mcpServers.PSObject.Properties.Name -contains "civil3d")) {
        $json.mcpServers.PSObject.Properties.Remove("civil3d")
        $json | ConvertTo-Json -Depth 20 | Set-Content -Encoding UTF8 $cfg
        Write-Host "[OK] removed civil3d from $cfg" -ForegroundColor Green
    }
}
