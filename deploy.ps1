# ══════════════════════════════════════════════════════════════════════════
# Backend deploy: stop service, pull latest, build, apply pending migrations,
# restart, health-check. Stops immediately on any failure instead of leaving
# the service half-updated (that's exactly what caused the 2026-09-27 outage).
# Usage: open PowerShell as Administrator in this folder and run: .\deploy.ps1
# ══════════════════════════════════════════════════════════════════════════
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$serviceName = 'ClinicSaaSAPI'
$healthUrl = 'http://192.168.194.59:5192/api/version'

function Step($msg) { Write-Host "`n== $msg ==" -ForegroundColor Cyan }

Step "Stopping service"
Stop-Service $serviceName -Force -ErrorAction SilentlyContinue
Get-Process ClinicSaaS.API -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 1

Step "Pulling latest code from main"
Set-Location $root
git pull origin main

Step "Building (Release)"
dotnet build "$root\ClinicSaaS.API.csproj" -c Release
if ($LASTEXITCODE -ne 0) {
    Write-Host "`nBUILD FAILED - stopping here on purpose. Service stays down, not restarted with broken code." -ForegroundColor Red
    exit 1
}

Step "Applying any pending migrations to the real database"
# Explicitly match the service's real environment so we read the correct
# appsettings.Development.json instead of falling back to something else.
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet ef database update
$migrateExit = $LASTEXITCODE
Remove-Item Env:\ASPNETCORE_ENVIRONMENT
if ($migrateExit -ne 0) {
    Write-Host "`nMIGRATION FAILED - check the error above. Service stays down on purpose." -ForegroundColor Red
    exit 1
}

Step "Starting service"
Start-Service $serviceName
Start-Sleep -Seconds 3

Step "Health check"
try {
    $resp = Invoke-RestMethod -Uri $healthUrl -TimeoutSec 8
    Write-Host "OK - API is up, deployed commit: $($resp.commit)" -ForegroundColor Green
} catch {
    Write-Host "FAILED - API did not respond at $healthUrl. Check the log now:" -ForegroundColor Red
    Get-Content "$root\stderr.log" -Tail 25
    exit 1
}
