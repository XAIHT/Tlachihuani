# Tlachihuani - Angela Lopez Mendoza - XAIHT
# Preview the rebuilt viewer against the existing log. No services are started.
param([string]$LogFile = (Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'XAIHT\Tlachihuani\Tlachihuani.log'))
$ErrorActionPreference = 'Stop'
$previewRoot = Split-Path -Parent $PSScriptRoot
$previewRun = Join-Path $previewRoot ('artifacts\log-viewer-preview-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 6))
$previewScratch = Join-Path $previewRun 'scratch'
[void][IO.Directory]::CreateDirectory($previewScratch)
$previewSaved = @{}
foreach ($name in @('TEMP', 'TMP', 'TLACHIHUANI_LOG_LEVEL')) { $previewSaved[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
$previewTranscript = $false
try {
    $env:TEMP = $previewScratch
    $env:TMP = $previewScratch
    $env:TLACHIHUANI_LOG_LEVEL = 'off'
    Start-Transcript -LiteralPath (Join-Path $previewRun 'preview.log') | Out-Null
    $previewTranscript = $true
    . (Join-Path $PSScriptRoot 'Assert-Visible.ps1')
    Assert-VisibleDevelopmentConsole -TimeoutSeconds 45
    $previewDll = Join-Path $previewRoot 'tests\Tlachihuani.Checks\bin\Release\net10.0-windows\Tlachihuani.Checks.dll'
    $previewRecord = Join-Path $previewRoot 'artifacts\log-viewer-preview.json'
    if (Test-Path -LiteralPath $previewRecord -PathType Leaf) {
        $previewDll = [IO.Path]::GetFullPath((Get-Content -LiteralPath $previewRecord -Raw | ConvertFrom-Json).assembly)
        $previewAllowed = [IO.Path]::GetFullPath((Join-Path $previewRoot 'tests\Tlachihuani.Checks\bin\'))
        if (-not $previewDll.StartsWith($previewAllowed, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($previewDll) -ne 'Tlachihuani.Checks.dll') {
            throw 'The preview record does not identify a checks build in this repository.'
        }
    }
    if (-not (Test-Path -LiteralPath $previewDll -PathType Leaf)) { throw 'Build the focused checks first with scripts\Test-LogViewer.ps1.' }
    Write-Host ('Opening the read-only viewer: ' + $LogFile)
    & dotnet $previewDll --log-viewer-preview $LogFile
    Write-Host ('LOG_VIEWER_PREVIEW_EXIT_CODE=' + $LASTEXITCODE)
} finally {
    if ($previewTranscript) { Stop-Transcript | Out-Null }
    foreach ($entry in $previewSaved.GetEnumerator()) { [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value, 'Process') }
}
