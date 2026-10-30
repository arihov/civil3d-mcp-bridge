<#
.SYNOPSIS
  Rebuild and redeploy only the add-in DLLs (no Python / Claude config changes).

.DESCRIPTION
  Close every Civil 3D window first (loaded DLLs are locked), then:
      powershell -ExecutionPolicy Bypass -File .\redeploy.ps1            # all installed releases
      powershell -ExecutionPolicy Bypass -File .\redeploy.ps1 -Releases 2027
  Thin wrapper over install.ps1 -SkipPython.
#>
[CmdletBinding()]
param([int[]]$Releases, [switch]$Prebuilt, [switch]$Net10For2026)

$p = @{ SkipPython = $true }
if ($Releases) { $p.Releases = $Releases }
if ($Prebuilt) { $p.Prebuilt = $true }
if ($Net10For2026) { $p.Net10For2026 = $true }
& (Join-Path $PSScriptRoot "install.ps1") @p
