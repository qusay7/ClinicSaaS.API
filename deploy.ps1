# ══════════════════════════════════════════════════════════════════════════
# Backend deploy: stop service, pull latest, build, apply pending migrations
# (when run interactively - see note below), restart, health-check. Stops
# immediately on any failure instead of leaving the service half-updated
# (that's exactly what caused the 2026-09-27 outage).
# Usage: open PowerShell as Administrator in this folder and run: .\deploy.ps1
# Also runs unattended via CD (NT AUTHORITY\SYSTEM) on push to main, with
# migrations skipped there - see the LocalDB note at that step.
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
if ($LASTEXITCODE -ne 0) {
    Write-Host "`nGIT PULL FAILED - stopping here. The server likely still has uncommitted local changes (check 'git status'). Deploying now would silently rebuild the OLD code." -ForegroundColor Red
    exit 1
}
Write-Host "Now at commit: $(git rev-parse --short HEAD)" -ForegroundColor DarkGray

Step "Building (Release)"
dotnet build "$root\ClinicSaaS.API.csproj" -c Release
if ($LASTEXITCODE -ne 0) {
    Write-Host "`nBUILD FAILED - stopping here on purpose. Service stays down, not restarted with broken code." -ForegroundColor Red
    exit 1
}

Step "Restoring local dotnet tools (dotnet-ef)"
# A global "dotnet tool install" only lives under the account that ran it, so
# whichever account runs this script (an interactive user, a CI runner's own
# service account, ...) needs dotnet-ef resolvable without depending on any
# specific account's global tool state. The local manifest at
# .config/dotnet-tools.json makes `dotnet ef` resolve regardless of account.
dotnet tool restore
if ($LASTEXITCODE -ne 0) {
    Write-Host "`nTOOL RESTORE FAILED - stopping here. Service stays down on purpose." -ForegroundColor Red
    exit 1
}

# ✅ appsettings.Development.json points at (localdb)\MSSQLLocalDB — LocalDB
# is a per-interactive-user database engine by design (Microsoft's own docs:
# not for services/production). NT AUTHORITY\SYSTEM (the CD runner account)
# has no LocalDB instance of its own and never will, no matter the password —
# so skip migrations entirely when running as SYSTEM instead of failing the
# whole deploy on something no code change here can fix. Run them yourself,
# interactively, whenever you add a new migration (same as always).
# Detecting the SYSTEM account itself kept failing (WindowsIdentity.Name and
# $env:USERNAME both came back wrong under the Actions runner's child
# process - it sanitizes/overrides the env it hands to job steps). Check for
# the actual thing that matters instead: are we inside GitHub Actions at
# all. GITHUB_ACTIONS is set to 'true' by the platform itself on every job,
# on every OS - that's the one signal guaranteed not to lie here.
if ($env:GITHUB_ACTIONS -eq 'true') {
    Write-Host "`nSkipping migrations - running inside GitHub Actions (CD), whose runner account has no LocalDB instance. Run 'dotnet ef database update' yourself interactively if this deploy includes a new migration." -ForegroundColor Yellow
} else {
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
