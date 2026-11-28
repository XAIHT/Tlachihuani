# Tlachihuani — Angela López Mendoza · XAIHT
# Visible check of the Services panel, Stop Services, HandGI, HandGI's windows and Flow Pills.
#
# Build Tlachihuani.slnx in Release, then run from a visible PowerShell window:
#   .\tests\desktop\ServicesAndHandGI.ps1
#
# HandGI and Flow Pills run in test folders of their own (TLACHIHUANI_USER_DIR), so the real ones
# are never touched, and HandGI runs with its camera off. No installed service is stopped: the
# Windows services in these checks are fakes. Whole-desktop photos are taken by Tlamatini's Shoter
# agent (tests\integration\rc_shoter_photo.py). Everything shown on screen is in <Output>\run.log.
param([string]$Output, [string]$Python = 'C:\Development\XAIHT\Tlamatini\python\python.exe')
# Exit codes: 0 every step passed, 1 a step failed, 2 not built yet, 3 the screen is locked.
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\..\..\scripts\Assert-Visible.ps1"
Assert-VisibleDevelopmentConsole
$Host.UI.RawUI.WindowTitle = 'Tlachihuani checks - Services and HandGI'
$root = (Resolve-Path "$PSScriptRoot\..\..").Path
if (!$Output) { $Output = Join-Path $root ('artifacts\verification\services-handgi-' + (Get-Date -Format 'yyyyMMdd-HHmmss')) }
New-Item -ItemType Directory -Force $Output | Out-Null
$log = Join-Path $Output 'run.log'
$checks = Join-Path $root 'tests\Tlachihuani.Checks\bin\Release\net10.0-windows\Tlachihuani.Checks.exe'
$handgi = Join-Path $root 'src\Tlachihuani.HandGI\bin\Release\net10.0-windows10.0.19041.0\Tlachihuani.HandGI.exe'
$tray = Join-Path $root 'src\Tlachihuani.Tray\bin\Release\net10.0-windows\Tlachihuani.Tray.exe'
$handgiChecks = Join-Path $root 'tests\Tlachihuani.HandGI.Checks\bin\Release\net10.0-windows10.0.19041.0\Tlachihuani.HandGI.Checks.exe'
foreach ($required in $checks, $handgiChecks, $handgi, $tray, $Python) {
    if (!(Test-Path -LiteralPath $required -PathType Leaf)) { Write-Host "STOPPED: $required is missing. Build Tlachihuani.slnx in Release first." -ForegroundColor Yellow; exit 2 }
}
$env:TLACHIHUANI_CHECKS_PHOTOS = Join-Path $Output 'photos'
$env:TLACHIHUANI_CHECKS_PYTHON = $Python
$env:TLACHIHUANI_CHECKS_SHOTER = Join-Path $root 'tests\integration\rc_shoter_photo.py'
$session = (Get-Process -Id $PID).SessionId

# A locked screen hides every window: a check nobody can watch is not run.
function Assert-Unlocked {
    if (Get-Process -Name LogonUI -ErrorAction SilentlyContinue | Where-Object SessionId -eq $session) {
        'STOPPED: the screen is locked, so nobody can watch the checks. Unlock it and run this script again.' | Tee-Object -FilePath $log -Append
        exit 3
    }
}

$results = @()
function Step([string]$title, [string[]]$arguments, [string]$program = $checks) {
    Assert-Unlocked
    "[$(Get-Date -Format o)] === $title ===" | Tee-Object -FilePath $log -Append
    # A check's own error lines belong in its record; they are not a reason to stop the run.
    $ErrorActionPreference = 'Continue'
    & $program @arguments 2>&1 | ForEach-Object { "$_" } | Tee-Object -FilePath $log -Append
    $code = $LASTEXITCODE
    "EXIT($title)=$code" | Tee-Object -FilePath $log -Append
    $script:results += [pscustomobject]@{ Step = $title; Exit = $code }
}

Step 'Unit checks' @()
Step 'HandGI lifecycle (test folder, camera off)' @('--handgi-lifecycle', $handgi)
Step 'Flow Pills lifecycle (test folder)' @('--flow-pills-lifecycle', $tray)
Step 'Services panel layout, desktop DPI' @('--tray-layout', (Join-Path $Output 'layout-desktop'), 'desktop')
Step 'Services panel layout, 96 DPI' @('--tray-layout', (Join-Path $Output 'layout-96dpi'), '96dpi')
Step 'HandGI windows layout, desktop DPI (camera off)' @('--layout', 'desktop') $handgiChecks
Step 'HandGI windows layout, 96 DPI (camera off)' @('--layout', '96dpi') $handgiChecks
'' | Tee-Object -FilePath $log -Append
$results | Format-Table -AutoSize | Out-String | Tee-Object -FilePath $log -Append
$failed = @($results | Where-Object { $_.Exit -ne 0 }).Count
"VERDICT: $(if ($failed -eq 0) { 'ALL STEPS PASSED' } else { "$failed STEP(S) FAILED" })" | Tee-Object -FilePath $log -Append
"[$(Get-Date -Format o)] finished; photos in $($env:TLACHIHUANI_CHECKS_PHOTOS)" | Tee-Object -FilePath $log -Append
if ($failed -ne 0) { exit 1 }
