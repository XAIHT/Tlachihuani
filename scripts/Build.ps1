param([ValidateSet('Build', 'Check', 'Publish')][string]$Mode = 'Check', [string]$PublishDirectory)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\Assert-Visible.ps1"
Assert-VisibleDevelopmentConsole
$projectRoot = Split-Path -Parent $PSScriptRoot
if (!$PublishDirectory) { $PublishDirectory = Join-Path $projectRoot 'artifacts\publish' }
Push-Location -LiteralPath $projectRoot
try {
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    dotnet restore Tlachihuani.slnx --locked-mode
    if ($LASTEXITCODE -ne 0) { throw "Restore failed: $LASTEXITCODE" }
    dotnet build Tlachihuani.slnx --configuration Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw "Build failed: $LASTEXITCODE" }
    if ($Mode -eq 'Check') {
        & '.\tests\Tlachihuani.Checks\bin\Release\net10.0-windows\Tlachihuani.Checks.exe' --service-exe '.\src\Tlachihuani.Service\bin\Release\net10.0-windows\Tlachihuani.Service.exe'
        if ($LASTEXITCODE -ne 0) { throw "Checks failed: $LASTEXITCODE" }
        & '.\tests\Tlachihuani.RC.Checks\bin\Release\net10.0-windows\Tlachihuani.RC.Checks.exe' --output '.\artifacts\rc-checks'
        if ($LASTEXITCODE -ne 0) { throw "RC checks failed: $LASTEXITCODE" }
    }
    if ($Mode -eq 'Publish') {
        foreach ($component in @('Service', 'Control', 'Tray', 'Jobs', 'RC.Server', 'HandGI')) {
            # Private runtimes travel with each executable. No client SDK/runtime
            # install or self-extraction into a user's TEMP directory is needed.
            $folder = if ($component -eq 'RC.Server') { 'RCServer' } else { $component }
            dotnet publish "src/Tlachihuani.$component/Tlachihuani.$component.csproj" --configuration Release --runtime win-x64 --self-contained true -p:RestoreLockedMode=true --output (Join-Path $PublishDirectory $folder)
            if ($LASTEXITCODE -ne 0) { throw "$component publish failed: $LASTEXITCODE" }
        }
    }
    if ($Mode -eq 'Publish') { & "$PSScriptRoot\Prepare-JobsRuntime.ps1" -Destination (Join-Path $PublishDirectory 'Jobs\runtime') }
    Write-Host "$Mode completed successfully." -ForegroundColor Green
} finally { Pop-Location }
