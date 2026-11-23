# Tlachihuani - Angela Lopez Mendoza - XAIHT
# Run from a visible PowerShell window. Only synthetic log files are changed.
$ErrorActionPreference = 'Stop'
$viewerRoot = Split-Path -Parent $PSScriptRoot
$viewerRun = Join-Path $viewerRoot ('artifacts\log-viewer-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 6))
$viewerScratch = Join-Path $viewerRun 'scratch'
$viewerConfiguration = 'LogViewer' + (Get-Date -Format 'yyyyMMddHHmmss') + [guid]::NewGuid().ToString('N').Substring(0, 6)
$viewerDll = Join-Path $viewerRoot ('tests\Tlachihuani.Checks\bin\' + $viewerConfiguration + '\net10.0-windows\Tlachihuani.Checks.dll')
[void][IO.Directory]::CreateDirectory($viewerScratch)
$viewerSaved = @{}
foreach ($name in @('TEMP', 'TMP', 'TLACHIHUANI_USER_DIR', 'TLACHIHUANI_LOG_LEVEL', 'DOTNET_CLI_TELEMETRY_OPTOUT', 'DOTNET_NOLOGO')) {
    $viewerSaved[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}
$viewerExit = 1
$viewerStage = 'foreground guard'
$viewerTranscript = $false
try {
    $env:TEMP = $viewerScratch
    $env:TMP = $viewerScratch
    $env:TLACHIHUANI_USER_DIR = Join-Path $viewerRun 'user-data'
    $env:TLACHIHUANI_LOG_LEVEL = 'off'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_NOLOGO = '1'
    [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
    $OutputEncoding = [Console]::OutputEncoding
    Start-Transcript -LiteralPath (Join-Path $viewerRun 'verification.log') | Out-Null
    $viewerTranscript = $true
    Write-Host ('Evidence directory: ' + $viewerRun)
    . (Join-Path $PSScriptRoot 'Assert-Visible.ps1')
    Assert-VisibleDevelopmentConsole -TimeoutSeconds 45
    Write-Host 'VISIBLE_CONSOLE_VERIFIED'
    $viewerStage = 'build'
    # A separate optimized configuration keeps an already open preview intact.
    & dotnet build (Join-Path $viewerRoot 'tests\Tlachihuani.Checks\Tlachihuani.Checks.csproj') --configuration $viewerConfiguration --disable-build-servers -p:RestoreLockedMode=true -p:Optimize=true --tl:off | Tee-Object -FilePath (Join-Path $viewerRun 'build.log')
    $viewerExit = $LASTEXITCODE
    Write-Host ('BUILD_EXIT_CODE=' + $viewerExit)
    if ($viewerExit -ne 0) { throw 'Log viewer verification build failed.' }
    $viewerExit = 1
    Assert-VisibleDevelopmentConsole -TimeoutSeconds 45
    $viewerStage = 'focused checks'
    Write-Host 'Opening the log viewer verification window with synthetic data.'
    & dotnet $viewerDll --log-viewer (Join-Path $viewerRun 'fixtures') | Tee-Object -FilePath (Join-Path $viewerRun 'checks.log')
    $viewerExit = $LASTEXITCODE
    Write-Host ('LOG_VIEWER_TEST_EXIT_CODE=' + $viewerExit)
    if ($viewerExit -ne 0) { throw 'Focused log viewer checks failed.' }
    $viewerExit = 1
    Assert-VisibleDevelopmentConsole -TimeoutSeconds 45
    $viewerStage = 'diff check'
    & git -C $viewerRoot diff --check
    $viewerExit = $LASTEXITCODE
    if ($viewerExit -ne 0) { throw 'Source whitespace check failed.' }
    & git -C $viewerRoot status --short | Tee-Object -FilePath (Join-Path $viewerRun 'source-status.log')
    [ordered]@{ assembly = $viewerDll; evidence = $viewerRun } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $viewerRoot 'artifacts\log-viewer-preview.json') -Encoding UTF8
    $viewerStage = 'complete'
    Write-Host 'LOG_VIEWER_VERIFICATION=PASS'
} catch {
    Write-Host ('LOG_VIEWER_VERIFICATION=FAIL: ' + $_.Exception.Message) -ForegroundColor Red
} finally {
    [ordered]@{ stage = $viewerStage; exitCode = $viewerExit } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $viewerRun 'result.json') -Encoding UTF8
    if ($viewerTranscript) { Stop-Transcript | Out-Null }
    foreach ($entry in $viewerSaved.GetEnumerator()) { [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value, 'Process') }
    $global:LASTEXITCODE = $viewerExit
}
