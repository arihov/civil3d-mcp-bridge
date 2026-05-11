<#
.SYNOPSIS
  Quick health-check that hits the C# bridge endpoints directly.
  Run after install.ps1 with Civil 3D open and the add-in loaded.
#>

$ErrorActionPreference = "Stop"
$url = if ($env:CIVIL3D_BRIDGE_URL) { $env:CIVIL3D_BRIDGE_URL } else { "http://127.0.0.1:7800" }

Write-Host "Smoke test — bridge at $url" -ForegroundColor Cyan
Write-Host ""

# 1. /health — basic ping.
try {
    $health = Invoke-RestMethod -Uri "$url/health" -Method GET -TimeoutSec 5
    Write-Host "  [OK] /health  ok=$($health.ok) version=$($health.version) toolCount=$($health.toolCount)" -ForegroundColor Green
} catch {
    Write-Host "  [FAIL] /health did not respond. Is Civil 3D running with the add-in loaded?" -ForegroundColor Red
    Write-Host "        From the Civil 3D command line, run MCPSTATUS to confirm the listener." -ForegroundColor Red
    exit 1
}

# 2. /invoke for a read-only call.
$body = @{ tool = "civil3d_list_alignments"; args = @{} } | ConvertTo-Json
try {
    $res = Invoke-RestMethod -Uri "$url/invoke" -Method POST -Body $body -ContentType "application/json" -TimeoutSec 30
    if ($res.ok) {
        Write-Host "  [OK] civil3d_list_alignments → $($res.result.count) alignment(s)" -ForegroundColor Green
    } else {
        Write-Host "  [FAIL] civil3d_list_alignments returned ok=false: $($res.error)" -ForegroundColor Red
        exit 1
    }
} catch {
    Write-Host "  [FAIL] /invoke threw: $_" -ForegroundColor Red
    exit 1
}

# 3. /invoke for drawing info.
$body2 = @{ tool = "civil3d_get_drawing_info"; args = @{} } | ConvertTo-Json
$res2 = Invoke-RestMethod -Uri "$url/invoke" -Method POST -Body $body2 -ContentType "application/json" -TimeoutSec 30
if ($res2.ok) {
    Write-Host "  [OK] civil3d_get_drawing_info → $($res2.result.name)" -ForegroundColor Green
} else {
    Write-Host "  [FAIL] civil3d_get_drawing_info: $($res2.error)" -ForegroundColor Red
}

Write-Host ""
Write-Host "Smoke test complete." -ForegroundColor Cyan
