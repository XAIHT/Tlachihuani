# Tlachihuani — Angela Lopez Mendoza · XAIHT
# Run in an existing visible PowerShell console; this script opens no console.
[CmdletBinding()]
param(
    [switch]$IncludeBrowser,
    [switch]$IncludeOidc,
    [switch]$IncludeDocker
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
. (Join-Path $PSScriptRoot 'Assert-Visible.ps1')
Assert-VisibleDevelopmentConsole
$runId = (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
$outputRoot = Join-Path $repository ('artifacts\rc-hosted-validation\' + $runId)
$scratch = Join-Path $outputRoot 'scratch'
$certificates = Join-Path $outputRoot 'certificates'
$logs = Join-Path $outputRoot 'logs'
$profileRoot = [Environment]::GetFolderPath('UserProfile')
$profilesRoot = Split-Path -Parent $profileRoot
if ($outputRoot.StartsWith($profilesRoot.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Hosted validation artifacts must be outside Windows user profiles.'
}
New-Item -ItemType Directory -Path $scratch, $certificates, $logs -Force | Out-Null
$environmentNames = @(
    'TEMP', 'TMP', 'TMPDIR', 'DOTNET_CLI_HOME', 'DOTNET_SKIP_FIRST_TIME_EXPERIENCE',
    'DOTNET_CLI_TELEMETRY_OPTOUT', 'DOTNET_GENERATE_ASPNET_CERTIFICATE', 'NUGET_PACKAGES', 'NUGET_HTTP_CACHE_PATH', 'NUGET_SCRATCH',
    'TL_RC_HOSTED_CERT', 'TL_RC_HOSTED_KEY', 'TL_RC_HOSTED_PFX', 'TL_RC_HOSTED_PFX_PASSWORD',
    'TL_RC_HOSTED_TEST_OUTPUT', 'TL_RC_OIDC_TEST_OUTPUT', 'TL_RC_HOSTED_DOCKER_IMAGE', 'TL_RC_HOSTED_BROWSER', 'TL_RC_TEST_IMAGE', 'TL_RC_CLOUD_TEST_IMAGE'
)
$originalEnvironment = @{}
foreach ($name in $environmentNames) { $originalEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
$originalDirectory = Get-Location
$steps = [Collections.Generic.List[object]]::new()
$exitCode = 0
$failure = $null
$transcriptStarted = $false
$dockerImage = $null

function Invoke-HostedNative {
    param([string]$Name, [string]$File, [string[]]$Arguments, [int[]]$ExpectedExit = @(0))
    Assert-VisibleDevelopmentConsole
    Write-Host ('==> ' + $Name)
    $started = [DateTimeOffset]::UtcNow
    $previousPreference = $ErrorActionPreference
    try {
        # Windows PowerShell 5 turns benign native stderr into ErrorRecords.
        # The native exit code, not stderr presence, determines success.
        $ErrorActionPreference = 'Continue'
        $global:LASTEXITCODE = -1
        & $File @Arguments 2>&1 | Tee-Object -FilePath (Join-Path $logs ($Name + '.log')) | Out-Host
        $nativeExit = $LASTEXITCODE
    } finally { $ErrorActionPreference = $previousPreference }
    $passed = $ExpectedExit -contains $nativeExit
    $steps.Add([ordered]@{ name = $Name; exit_code = $nativeExit; expected_exit = $ExpectedExit; passed = $passed; started_at = $started.ToString('o'); finished_at = [DateTimeOffset]::UtcNow.ToString('o') })
    if (-not $passed) { throw ($Name + ' failed with exit code ' + $nativeExit + '. Read its retained log.') }
}

function Assert-HostedPortsFree {
    param([int[]]$Ports)
    $listeners = [Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners()
    $busy = @($listeners | Where-Object { $Ports -contains $_.Port } | ForEach-Object { $_.ToString() })
    if ($busy.Count -gt 0) { throw ('Test ports are already in use: ' + ($busy -join ', ') + '. No existing process was stopped.') }
}

function Assert-HostedEvidence {
    param([string]$Directory)
    $resultsPath = Join-Path $Directory 'results.json'
    $cleanupPath = Join-Path $Directory 'cleanup.json'
    if (-not (Test-Path -LiteralPath $resultsPath)) { throw 'The hosted harness did not write results.json.' }
    $result = Get-Content -LiteralPath $resultsPath -Raw | ConvertFrom-Json
    if ($result.failed -ne 0 -or $result.passed -le 0) { throw 'The hosted harness reported failures or no passing checks.' }
    if (-not (Test-Path -LiteralPath $cleanupPath)) { throw 'The hosted harness did not write cleanup.json.' }
    $cleanup = Get-Content -LiteralPath $cleanupPath -Raw | ConvertFrom-Json
    foreach ($property in $cleanup.PSObject.Properties) {
        if ($property.Value -is [bool] -and -not $property.Value) { throw ('Harness cleanup failed: ' + $property.Name) }
    }
}

try {
    Start-Transcript -Path (Join-Path $outputRoot 'transcript.txt') | Out-Null
    $transcriptStarted = $true
    Set-Location -LiteralPath $repository
    foreach ($name in @('TEMP', 'TMP', 'TMPDIR')) { [Environment]::SetEnvironmentVariable($name, $scratch, 'Process') }
    [Environment]::SetEnvironmentVariable('DOTNET_CLI_HOME', (Join-Path $scratch 'dotnet'), 'Process')
    [Environment]::SetEnvironmentVariable('DOTNET_SKIP_FIRST_TIME_EXPERIENCE', '1', 'Process')
    [Environment]::SetEnvironmentVariable('DOTNET_CLI_TELEMETRY_OPTOUT', '1', 'Process')
    [Environment]::SetEnvironmentVariable('DOTNET_GENERATE_ASPNET_CERTIFICATE', 'false', 'Process')
    $packageCache = Join-Path $repository 'artifacts\release-cache\packages'
    New-Item -ItemType Directory -Path $packageCache -Force | Out-Null
    [Environment]::SetEnvironmentVariable('NUGET_PACKAGES', $packageCache, 'Process')
    [Environment]::SetEnvironmentVariable('NUGET_HTTP_CACHE_PATH', (Join-Path $scratch 'nuget-http'), 'Process')
    [Environment]::SetEnvironmentVariable('NUGET_SCRATCH', (Join-Path $scratch 'nuget-scratch'), 'Process')
    $dotnet = (Get-Command dotnet -ErrorAction Stop).Source
    $node = (Get-Command node -ErrorAction Stop).Source
    $opensslCommand = Get-Command openssl -ErrorAction SilentlyContinue
    $openssl = if ($opensslCommand) { $opensslCommand.Source } else { $null }
    if (-not $openssl) {
        foreach ($candidate in @('C:\Program Files\Git\usr\bin\openssl.exe', 'C:\Program Files\OpenSSL-Win64\bin\openssl.exe')) {
            if (Test-Path -LiteralPath $candidate) { $openssl = $candidate; break }
        }
    }
    if (-not $openssl) { throw 'OpenSSL is required on PATH or in the installed Git/OpenSSL directory. No software was installed.' }
    if ($IncludeOidc -or $IncludeDocker) { $null = Get-Command docker -ErrorAction Stop }
    Assert-HostedPortsFree -Ports @(12487, 12490)
    $clientOutput = Join-Path $outputRoot 'client'
    [Environment]::SetEnvironmentVariable('TL_RC_HOSTED_TEST_OUTPUT', $clientOutput, 'Process')
    Invoke-HostedNative -Name 'client-checks' -File $node -Arguments @('tests/integration/rc_client_checks.mjs')
    Assert-HostedEvidence -Directory $clientOutput
    Invoke-HostedNative -Name 'restore-rc' -File $dotnet -Arguments @('restore', 'src/Tlachihuani.RC.Server/Tlachihuani.RC.Server.csproj', '--locked-mode')
    Invoke-HostedNative -Name 'restore-cloud' -File $dotnet -Arguments @('restore', 'src/Tlachihuani.RC.Cloud/Tlachihuani.RC.Cloud.csproj', '--locked-mode')
    Invoke-HostedNative -Name 'build-rc' -File $dotnet -Arguments @('build', 'src/Tlachihuani.RC.Server/Tlachihuani.RC.Server.csproj', '--configuration', 'Release', '--no-restore', '--disable-build-servers')
    Invoke-HostedNative -Name 'build-cloud' -File $dotnet -Arguments @('build', 'src/Tlachihuani.RC.Cloud/Tlachihuani.RC.Cloud.csproj', '--configuration', 'Release', '--no-restore', '--disable-build-servers')
    $certificatePath = Join-Path $certificates 'loopback-cert.pem'
    $keyPath = Join-Path $certificates 'loopback-key.pem'
    $pfxPath = Join-Path $certificates 'loopback-cert.pfx'
    $randomBytes = New-Object byte[] 32
    $random = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $random.GetBytes($randomBytes) } finally { $random.Dispose() }
    $pfxPassword = ([BitConverter]::ToString($randomBytes)).Replace('-', '').ToLowerInvariant()
    [Environment]::SetEnvironmentVariable('TL_RC_HOSTED_PFX_PASSWORD', $pfxPassword, 'Process')
    Invoke-HostedNative -Name 'certificate-create' -File $openssl -Arguments @('req', '-x509', '-newkey', 'rsa:3072', '-sha256', '-nodes', '-days', '2', '-subj', '/CN=Tlachihuani hosted development', '-addext', 'subjectAltName=IP:127.0.0.1,IP:127.0.0.2', '-keyout', $keyPath, '-out', $certificatePath)
    Invoke-HostedNative -Name 'certificate-pfx' -File $openssl -Arguments @('pkcs12', '-export', '-in', $certificatePath, '-inkey', $keyPath, '-out', $pfxPath, '-passout', 'env:TL_RC_HOSTED_PFX_PASSWORD')
    [Environment]::SetEnvironmentVariable('TL_RC_HOSTED_CERT', $certificatePath, 'Process')
    [Environment]::SetEnvironmentVariable('TL_RC_HOSTED_KEY', $keyPath, 'Process')
    [Environment]::SetEnvironmentVariable('TL_RC_HOSTED_PFX', $pfxPath, 'Process')
    [Environment]::SetEnvironmentVariable('TL_RC_TEST_IMAGE', (Join-Path $repository 'src\Tlachihuani.RC.Server\bin\Release\net10.0-windows\Tlachihuani.RC.Server.exe'), 'Process')
    [Environment]::SetEnvironmentVariable('TL_RC_CLOUD_TEST_IMAGE', (Join-Path $repository 'src\Tlachihuani.RC.Cloud\bin\Release\net10.0\Tlachihuani.RC.Cloud.exe'), 'Process')
    [Environment]::SetEnvironmentVariable('TL_RC_HOSTED_BROWSER', $(if ($IncludeBrowser) { '1' } else { $null }), 'Process')
    $apiOutput = Join-Path $outputRoot 'relay-and-browser'
    [Environment]::SetEnvironmentVariable('TL_RC_HOSTED_TEST_OUTPUT', $apiOutput, 'Process')
    Invoke-HostedNative -Name 'relay-and-browser' -File $node -Arguments @('tests/integration/rc_hosted_checks.mjs')
    Assert-HostedEvidence -Directory $apiOutput
    if ($IncludeOidc) {
        if (-not (Test-Path -LiteralPath 'tests/integration/rc_hosted_oidc.mjs')) { throw 'The OIDC harness is not present; no OIDC result is claimed.' }
        $oidcOutput = Join-Path $outputRoot 'oidc'
        Assert-HostedPortsFree -Ports @(12490, 12492)
        [Environment]::SetEnvironmentVariable('TL_RC_OIDC_TEST_OUTPUT', $oidcOutput, 'Process')
        Invoke-HostedNative -Name 'oidc' -File $node -Arguments @('tests/integration/rc_hosted_oidc.mjs')
        Assert-HostedEvidence -Directory $oidcOutput
    }
    if ($IncludeDocker) {
        $docker = (Get-Command docker -ErrorAction Stop).Source
        $dockerImage = 'tlachihuani-rc-cloud-development:' + $runId.ToLowerInvariant()
        Invoke-HostedNative -Name 'docker-build' -File $docker -Arguments @('build', '--file', 'src/Tlachihuani.RC.Cloud/Dockerfile', '--tag', $dockerImage, '.')
        Invoke-HostedNative -Name 'restore-container-checks' -File $dotnet -Arguments @('restore', 'tests/Tlachihuani.RC.ContainerChecks/Tlachihuani.RC.ContainerChecks.csproj', '--locked-mode')
        Invoke-HostedNative -Name 'build-container-checks' -File $dotnet -Arguments @('build', 'tests/Tlachihuani.RC.ContainerChecks/Tlachihuani.RC.ContainerChecks.csproj', '--configuration', 'Release', '--no-restore', '--disable-build-servers')
        $containerOutput = Join-Path $outputRoot 'container'
        [Environment]::SetEnvironmentVariable('TL_RC_HOSTED_TEST_OUTPUT', $containerOutput, 'Process')
        [Environment]::SetEnvironmentVariable('TL_RC_HOSTED_DOCKER_IMAGE', $dockerImage, 'Process')
        Invoke-HostedNative -Name 'docker-container-checks' -File $node -Arguments @('tests/integration/rc_hosted_container.mjs')
        Assert-HostedEvidence -Directory $containerOutput
    }
} catch {
    $exitCode = 1
    $failure = $_.Exception.Message
    Write-Host ('FAILED: ' + $failure) -ForegroundColor Red
} finally {
    Set-Location -LiteralPath $originalDirectory.Path
    foreach ($name in $environmentNames) { [Environment]::SetEnvironmentVariable($name, $originalEnvironment[$name], 'Process') }
    $summary = [ordered]@{
        scope = 'Development-only isolated loopback HTTPS; no installation, production deployment or real model acceptance'
        run_id = $runId; finished_at = [DateTimeOffset]::UtcNow.ToString('o'); exit_code = $exitCode
        requested = @{ browser = [bool]$IncludeBrowser; oidc = [bool]$IncludeOidc; docker = [bool]$IncludeDocker }
        steps = $steps.ToArray(); failure = $failure; docker_image = $dockerImage
        docker_boundary = 'Actual non-root read-only Linux HTTPS/BFF checks and default startup gate; no host ports, Internet or full relay acceptance'
    }
    $summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $outputRoot 'summary.json') -Encoding UTF8
    Write-Host ('Evidence: ' + $outputRoot)
    Write-Host 'This console remains open. Test harnesses own and close their isolated processes; no existing service is stopped.'
    if ($transcriptStarted) { Stop-Transcript | Out-Null }
    $global:LASTEXITCODE = $exitCode
}
if ($exitCode -ne 0) { throw ('Hosted development verification failed. Read ' + (Join-Path $outputRoot 'summary.json')) }
