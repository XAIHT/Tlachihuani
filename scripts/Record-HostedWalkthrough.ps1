param([string]$OutputRoot='C:/Development/XAIHT/repo/Tlachihuani/artifacts/rc-hosted-video/run-01')
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $repo
. "$PSScriptRoot/Assert-Visible.ps1"
Assert-VisibleDevelopmentConsole
if($OutputRoot -match '(?i)^[A-Z]:[\\/]Users[\\/]'){throw 'Use an application-owned output folder outside user profiles.'}
if(Test-Path -LiteralPath $OutputRoot){throw 'Use a fresh output directory for each recording.'}
New-Item -ItemType Directory -Force "$OutputRoot/scratch" | Out-Null
$env:TEMP=$env:TMP=$env:TMPDIR="$OutputRoot/scratch"
$py='C:/Development/XAIHT/Tlamatini/python/python.exe'
foreach($binary in @('src/Tlachihuani.RC.Server/bin/Release/net10.0-windows/Tlachihuani.RC.Server.exe','src/Tlachihuani.RC.Cloud/bin/Release/net10.0/Tlachihuani.RC.Cloud.exe')){
 if(-not(Test-Path -LiteralPath $binary)){throw "Build the Release development project first: $binary"}
}
& docker version --format '{{.Server.Version}}'
if($LASTEXITCODE -ne 0){throw 'Start Docker Desktop before recording.'}
& $py tests/tutorial/video_certificate.py "$OutputRoot/certificates"
if($LASTEXITCODE -ne 0){throw 'Certificate setup failed'}
$env:TL_RC_HOSTED_CERT="$OutputRoot/certificates/cert.pem"
$env:TL_RC_HOSTED_KEY="$OutputRoot/certificates/key.pem"
$env:TL_RC_HOSTED_PFX="$OutputRoot/certificates/cert.pfx"
$env:TL_RC_HOSTED_PFX_PASSWORD=[IO.File]::ReadAllText("$OutputRoot/certificates/password.txt")
$env:TL_RC_VIDEO_OUTPUT=$OutputRoot
& node --check tests/tutorial/record_hosted_walkthrough.mjs
if($LASTEXITCODE -ne 0){throw 'Recording harness syntax failed'}
& node tests/tutorial/record_hosted_walkthrough.mjs
if($LASTEXITCODE -ne 0){throw "Recording checks failed. See $OutputRoot/results.json"}
Write-Host "Recorded walkthrough and verification evidence: $OutputRoot"
