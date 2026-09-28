# ══════════════════════════════════════════════════════════════════════════
# نشر الباك اند: إيقاف الـ service، سحب آخر كود، بناء، تطبيق أي migration
# معلّقة، وإعادة التشغيل — مع توقف فوري عند أول خطأ (بدل ما نكمل بكود قديم
# مبني جزئياً أو قاعدة بيانات نص محدّثة، هذا بالضبط اللي سبب أزمة يوم 2026-09-27)
# الاستخدام: افتح PowerShell كـ Administrator بهاد المجلد وشغّل: .\deploy.ps1
# ══════════════════════════════════════════════════════════════════════════
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$serviceName = 'ClinicSaaSAPI'
$healthUrl = 'http://192.168.194.59:5192/api/version'

function Step($msg) { Write-Host "`n== $msg ==" -ForegroundColor Cyan }

Step "إيقاف الـ service"
Stop-Service $serviceName -Force -ErrorAction SilentlyContinue
Get-Process ClinicSaaS.API -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 1

Step "سحب آخر كود من main"
Set-Location $root
git pull origin main

Step "بناء المشروع (Release)"
dotnet build "$root\ClinicSaaS.API.csproj" -c Release
if ($LASTEXITCODE -ne 0) {
    Write-Host "`n❌ فشل البناء — توقفنا هون عمداً. الـ service لسا واقف، ما رح نشغّله بكود مكسور." -ForegroundColor Red
    exit 1
}

Step "تطبيق أي migration معلّقة على قاعدة البيانات الحقيقية"
# ✅ نحدد البيئة صراحة (نفس بيئة الـ service الفعلية) عشان نضمن قراءة
# appsettings.Development.json الصحيح، لا الرجوع لأي fallback مختلف
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet ef database update
$migrateExit = $LASTEXITCODE
Remove-Item Env:\ASPNETCORE_ENVIRONMENT
if ($migrateExit -ne 0) {
    Write-Host "`n❌ فشل تطبيق الـ migration — راجع الخطأ فوق. الـ service لسا واقف عمداً." -ForegroundColor Red
    exit 1
}

Step "تشغيل الـ service"
Start-Service $serviceName
Start-Sleep -Seconds 3

Step "فحص الصحة"
try {
    $resp = Invoke-RestMethod -Uri $healthUrl -TimeoutSec 8
    Write-Host "✅ الـ API شغال — commit المنشور: $($resp.commit)" -ForegroundColor Green
} catch {
    Write-Host "❌ الـ API ما رد على $healthUrl — افحص اللوغ فوراً:" -ForegroundColor Red
    Get-Content "$root\stderr.log" -Tail 25
    exit 1
}
