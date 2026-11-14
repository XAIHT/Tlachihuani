# Actual SCM acceptance of the real RC host. The production registrations are read-only.
param([Parameter(Mandatory=$true)][string]$OperatorSid)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$work = Join-Path $projectRoot 'artifacts\rc-implementation'
$env:TEMP = Join-Path $work 'scratch'
$env:TMP = $env:TEMP
$Host.UI.RawUI.WindowTitle = 'Tlachihuani RC isolated SCM acceptance - KEEP OPEN'
Start-Transcript -LiteralPath (Join-Path $work 'scm-session.log') -Append
. (Join-Path $projectRoot 'scripts\Assert-Visible.ps1')
Assert-VisibleDevelopmentConsole -TimeoutSeconds 300
$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (!$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'SCM acceptance requires an elevated visible console.' }
[void][Security.Principal.SecurityIdentifier]::new($OperatorSid)
$service = 'Tlachihuani.RemoteController.Acceptance'
$image = Join-Path $work 'publish\RCServer\Tlachihuani.RC.Server.exe'
if (!(Test-Path -LiteralPath $image -PathType Leaf)) { throw 'Build the self-contained RC acceptance payload first.' }
if (Get-Service -Name $service -ErrorAction SilentlyContinue) { throw 'An acceptance service already exists; refusing to replace it.' }
$nonce = [guid]::NewGuid().ToString('N')
$data = Join-Path $env:ProgramData ('XAIHT\Acceptance\rc-scm-' + $nonce)
if (Test-Path -LiteralPath $data) { throw 'Acceptance data already exists.' }
function Plain([string]$Path) {
    $current=[IO.Path]::GetFullPath($Path)
    while($current) { if((Test-Path -LiteralPath $current) -and ((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Acceptance path contains a reparse point.'};$current=[IO.Path]::GetDirectoryName($current) }
}
Plain $image
Plain $data
$before = @(Get-CimInstance Win32_Service -Filter "Name='Tlachihuani' OR Name='Tlachihuani.Jobs'" | Select-Object Name,State,ProcessId,StartMode,PathName)
$checks = [Collections.Generic.List[object]]::new()
$created = $false
function Check([string]$Name,[bool]$Condition) { if(!$Condition){throw "FAIL SCM: $Name"};Write-Host "PASS SCM: $Name" -ForegroundColor Green;$checks.Add(@{name=$Name;passed=$true}) }
function Protect([string]$Path,[string]$Sid,[string]$Rights) {
    $acl=[Security.AccessControl.DirectorySecurity]::new()
    $acl.SetAccessRuleProtection($true,$false)
    foreach($entry in @(@('S-1-5-18','FullControl'),@('S-1-5-32-544','FullControl'),@($OperatorSid,'ReadAndExecute'),@($Sid,$Rights))) {
        $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new([Security.Principal.SecurityIdentifier]::new($entry[0]),$entry[1],'ContainerInherit,ObjectInherit','None','Allow'))
    }
    Set-Acl -LiteralPath $Path -AclObject $acl
}
try {
    $arguments='"'+$image+'" --scm-acceptance "'+$data+'" '+$nonce+' 12485 '+$OperatorSid
    [void](New-Service -Name $service -DisplayName 'Remote-Controller isolated acceptance' -BinaryPathName $arguments -StartupType Manual)
    $created=$true
    & sc.exe config $service obj= ('NT SERVICE\'+$service) start= demand
    if($LASTEXITCODE){throw 'Virtual account configuration failed'}
    & sc.exe sidtype $service unrestricted
    if($LASTEXITCODE){throw 'Service SID configuration failed'}
    $svcSid=([Security.Principal.NTAccount]::new('NT SERVICE\'+$service)).Translate([Security.Principal.SecurityIdentifier]).Value
    [void][IO.Directory]::CreateDirectory($data)
    Protect $data $svcSid 'Modify'
    Check 'New service remains stopped' ((Get-Service $service).Status -eq 'Stopped')
    Check 'New service remains Manual' ((Get-CimInstance Win32_Service -Filter "Name='$service'").StartMode -eq 'Manual')
    Check 'Dedicated virtual account configured' ((Get-CimInstance Win32_Service -Filter "Name='$service'").StartName -eq ('NT SERVICE\'+$service))
    Start-Service $service
    (Get-Service $service).WaitForStatus('Running',[TimeSpan]::FromSeconds(30))
    $running=Get-CimInstance Win32_Service -Filter "Name='$service'"
    $owned=Get-Process -Id $running.ProcessId
    Check 'SCM reports running' ($running.State -eq 'Running')
    Check 'SCM owns the expected executable' ([IO.Path]::GetFullPath($owned.Path) -eq [IO.Path]::GetFullPath($image))
    $ready=$false
    for($i=0;$i -lt 60;$i++){try{$ready=(Invoke-RestMethod 'http://127.0.0.1:12485/api/rc/v1/health' -TimeoutSec 2).ready}catch{};if($ready){break};Start-Sleep -Milliseconds 250}
    Check 'Real SCM-hosted HTTP listener becomes ready' $ready
    Check 'Complete client is served from SCM host' ((Invoke-WebRequest 'http://127.0.0.1:12485/remote.html' -UseBasicParsing).StatusCode -eq 200)
    $processBefore=$running.ProcessId
    Stop-Service $service
    (Get-Service $service).WaitForStatus('Stopped',[TimeSpan]::FromSeconds(30))
    Check 'SCM confirms graceful stop' ((Get-Service $service).Status -eq 'Stopped')
    Check 'Owned process exits after stop' ($owned.WaitForExit(10000))
    $closed=$false;try{[void](Invoke-WebRequest 'http://127.0.0.1:12485/api/rc/v1/health' -UseBasicParsing -TimeoutSec 2)}catch{$closed=$true}
    Check 'Stop releases the RC listener' $closed
    Start-Service $service
    (Get-Service $service).WaitForStatus('Running',[TimeSpan]::FromSeconds(30))
    $next=Get-CimInstance Win32_Service -Filter "Name='$service'"
    Check 'Explicit start creates a new process' ($next.ProcessId -ne $processBefore)
    Stop-Service $service
    (Get-Service $service).WaitForStatus('Stopped',[TimeSpan]::FromSeconds(30))
    Check 'Second stop leaves service stopped' ((Get-Service $service).Status -eq 'Stopped')
    $after=@(Get-CimInstance Win32_Service -Filter "Name='Tlachihuani' OR Name='Tlachihuani.Jobs'" | Select-Object Name,State,ProcessId,StartMode,PathName)
    Check 'Original service and Torrenter registrations stay unchanged' (($before|ConvertTo-Json -Compress) -eq ($after|ConvertTo-Json -Compress))
    Check 'Durable state remains in isolated service-owned storage' (Test-Path -LiteralPath (Join-Path $data 'state\state.json'))
} catch { $checks.Add(@{name='SCM acceptance failure';passed=$false;error=$_.Exception.ToString()});Write-Host $_ -ForegroundColor Red }
finally {
    if($created){
        $registered=Get-CimInstance Win32_Service -Filter "Name='$service'"
        if($registered.PathName -ne $arguments){throw 'Acceptance registration identity changed; refusing cleanup'}
        if((Get-Service $service).Status -ne 'Stopped'){Stop-Service $service;(Get-Service $service).WaitForStatus('Stopped',[TimeSpan]::FromSeconds(30))}
        & sc.exe delete $service
        if($LASTEXITCODE){throw 'Acceptance service removal failed'}
    }
    $checks | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $work 'scm-results.json') -Encoding UTF8
    Write-Host ('SCM acceptance: '+@($checks|Where-Object passed).Count+'/'+$checks.Count+' passed. Isolated data retained at '+$data)
    Stop-Transcript
}
