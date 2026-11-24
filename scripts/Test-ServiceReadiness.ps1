# Tlachihuani - Angela Lopez Mendoza - XAIHT
# Run from a visible PowerShell window. Only isolated fixtures are changed; installed services are not controlled.
$ErrorActionPreference = 'Stop'
$readinessRoot = Split-Path -Parent $PSScriptRoot
$readinessRun = Join-Path $readinessRoot ('artifacts\service-readiness-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 6))
$readinessScratch = Join-Path $readinessRun 'scratch'
$readinessConfiguration = 'ServiceReadiness' + (Get-Date -Format 'yyyyMMddHHmmss') + [guid]::NewGuid().ToString('N').Substring(0, 6)
$readinessDll = Join-Path $readinessRoot ('tests\Tlachihuani.Checks\bin\' + $readinessConfiguration + '\net10.0-windows\Tlachihuani.Checks.dll')
[void][IO.Directory]::CreateDirectory($readinessScratch)
$readinessSaved = @{}
foreach ($name in @('PATH', 'TEMP', 'TMP', 'TLACHIHUANI_USER_DIR', 'TLACHIHUANI_LOG_LEVEL', 'DOTNET_CLI_TELEMETRY_OPTOUT', 'DOTNET_NOLOGO')) {
    $readinessSaved[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}
$readinessExit = 1
$readinessStage = 'foreground guard'
$readinessTranscript = $false
try {
    $env:TEMP = $readinessScratch
    $env:TMP = $readinessScratch
    $env:TLACHIHUANI_USER_DIR = Join-Path $readinessRun 'user-data'
    $env:TLACHIHUANI_LOG_LEVEL = 'off'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_NOLOGO = '1'
    [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
    $OutputEncoding = [Console]::OutputEncoding
    Start-Transcript -LiteralPath (Join-Path $readinessRun 'verification.log') | Out-Null
    $readinessTranscript = $true
    Write-Host ('Evidence directory: ' + $readinessRun)
    . (Join-Path $PSScriptRoot 'Assert-Visible.ps1')
    Assert-VisibleDevelopmentConsole -TimeoutSeconds 45
    Write-Host 'VISIBLE_CONSOLE_VERIFIED'
    $readinessDotnet = 'C:\Program Files\dotnet\dotnet.exe'
    $readinessGit = 'C:\Program Files\Git\cmd\git.exe'
    $readinessStage = 'restore focused test graph'
    & $readinessDotnet restore (Join-Path $readinessRoot 'tests\Tlachihuani.Checks\Tlachihuani.Checks.csproj') -p:RestoreLockedMode=true
    if ($LASTEXITCODE -ne 0) { throw 'Focused test dependency restore failed.' }
    $readinessStage = 'build'
    # A separate optimized configuration keeps an already open preview intact.
    & $readinessDotnet build (Join-Path $readinessRoot 'tests\Tlachihuani.Checks\Tlachihuani.Checks.csproj') --configuration $readinessConfiguration --disable-build-servers -p:RestoreLockedMode=true -p:Optimize=true --tl:off | Tee-Object -FilePath (Join-Path $readinessRun 'build.log')
    $readinessExit = $LASTEXITCODE
    Write-Host ('BUILD_EXIT_CODE=' + $readinessExit)
    if ($readinessExit -ne 0) { throw 'Service readiness verification build failed.' }
    $readinessExit = 1
    Assert-VisibleDevelopmentConsole -TimeoutSeconds 45
    $readinessStage = 'focused checks'
    Write-Host 'Verifying readiness and opening the diagnostic report fixture.'
    & $readinessDotnet $readinessDll --service-readiness (Join-Path $readinessRun 'fixtures') | Tee-Object -FilePath (Join-Path $readinessRun 'checks.log')
    $readinessExit = $LASTEXITCODE
    Write-Host ('SERVICE_READINESS_TEST_EXIT_CODE=' + $readinessExit)
    if ($readinessExit -ne 0) { throw 'Focused service readiness checks failed.' }
    $readinessExit = 1
    Assert-VisibleDevelopmentConsole -TimeoutSeconds 45
    $readinessStage = 'existing lifecycle and IPC checks'
    & $readinessDotnet $readinessDll 2>&1 | Tee-Object -FilePath (Join-Path $readinessRun 'core-checks.log')
    $readinessExit = $LASTEXITCODE
    if ($readinessExit -ne 0) { throw 'Existing lifecycle and IPC checks failed.' }
    $readinessStage = 'diff check'
    & $readinessGit --no-pager -C $readinessRoot diff --check 2>&1 | Tee-Object -FilePath (Join-Path $readinessRun 'diff-check.log')
    $readinessExit = $LASTEXITCODE
    if ($readinessExit -ne 0) { throw 'Source whitespace check failed.' }
    & $readinessGit -C $readinessRoot status --short | Tee-Object -FilePath (Join-Path $readinessRun 'source-status.log')
    [ordered]@{ assembly = $readinessDll; evidence = $readinessRun } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $readinessRoot 'artifacts\service-readiness-verified.json') -Encoding UTF8
    $readinessStage = 'complete'
    Write-Host 'SERVICE_READINESS_VERIFICATION=PASS'
} catch {
    Write-Host ('SERVICE_READINESS_VERIFICATION=FAIL: ' + $_.Exception.Message) -ForegroundColor Red
} finally {
    [ordered]@{ stage = $readinessStage; exitCode = $readinessExit } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $readinessRun 'result.json') -Encoding UTF8
    if ($readinessTranscript) { Stop-Transcript | Out-Null }
    foreach ($entry in $readinessSaved.GetEnumerator()) { [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value, 'Process') }
    $global:LASTEXITCODE = $readinessExit
}
