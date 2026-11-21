# Tlachihuani — Angela López Mendoza · XAIHT
# Verbose log of every Tlachihuani script. Assert-Visible.ps1 dot-sources this file, so each
# script that checks for its visible console also records, from that moment:
#   <script>-<date>-<time>-p<pid>.log        everything the script shows (a transcript)
#   <script>-<date>-<time>-p<pid>-lines.log  every statement line it executes, as it runs
# in %LOCALAPPDATA%\XAIHT\Tlachihuani\script-logs. XT copies each line into Tlachihuani.log
# (stamped, secrets masked).
# The executed lines come from a silent breakpoint on every statement line: its action
# writes the line and never stops the script, and nothing extra appears in the console.
# (Windows PowerShell does not put Set-PSDebug trace lines into a transcript.)
# TLACHIHUANI_LOG_LEVEL=off records nothing; any level other than trace keeps only the transcript.
function Start-TlachihuaniScriptTrace {
    param([string]$Script)
    # Works under Set-StrictMode -Version Latest (no unset variable is ever read).
    if (-not $Script) { return }
    $traces = Get-Variable -Name TlachihuaniScriptTraces -Scope Global -ValueOnly -ErrorAction SilentlyContinue
    if ($null -eq $traces) { $traces = @{}; Set-Variable -Name TlachihuaniScriptTraces -Scope Global -Value $traces }
    if ($traces.ContainsKey($Script)) { return }
    $level = ([string][Environment]::GetEnvironmentVariable('TLACHIHUANI_LOG_LEVEL')).Trim().ToLowerInvariant()
    if ($level -eq 'off') { return }
    $custom = [Environment]::GetEnvironmentVariable('TLACHIHUANI_USER_DIR')
    $folder = if ($custom) { Join-Path ([IO.Path]::GetFullPath($custom)) 'script-logs' }
              else { Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'XAIHT\Tlachihuani\script-logs' }
    try {
        [void][IO.Directory]::CreateDirectory($folder)
        $stem = [IO.Path]::GetFileNameWithoutExtension($Script) + '-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-p' + $PID
        $file = Join-Path $folder ($stem + '.log')
        # One transcript per process: it already records what nested scripts show, and a
        # second one would sit on top of a caller's own Start/Stop-Transcript pair.
        if ($traces.Count -eq 0) {
            Start-Transcript -LiteralPath $file -Append -IncludeInvocationHeader | Out-Null
            Write-Host ('Tlachihuani script log: ' + $file + '   (XT copies it into Tlachihuani.log)')
        }
        $traces[$Script] = $file
        if ($level -notin @('', 'trace')) { return }

        $writer = Get-Variable -Name TlachihuaniScriptLineWriter -Scope Global -ValueOnly -ErrorAction SilentlyContinue
        if ($null -eq $writer) {
            $writer = [IO.StreamWriter]::new((Join-Path $folder ($stem + '-lines.log')), $true, [Text.UTF8Encoding]::new($false))
            $writer.AutoFlush = $true
            Set-Variable -Name TlachihuaniScriptLineWriter -Scope Global -Value $writer
            Set-Variable -Name TlachihuaniScriptLines -Scope Global -Value @{}
        }
        $known = Get-Variable -Name TlachihuaniScriptLines -Scope Global -ValueOnly
        $source = [IO.File]::ReadAllLines($Script)
        $tokens = $null; $errors = $null
        $ast = [Management.Automation.Language.Parser]::ParseFile($Script, [ref]$tokens, [ref]$errors)
        $lines = $ast.FindAll({ param($node) $node -is [Management.Automation.Language.StatementAst] }, $true) |
            ForEach-Object { $_.Extent.StartLineNumber } | Sort-Object -Unique
        $leaf = [IO.Path]::GetFileName($Script)
        # [char]0xB7 is the middle dot of the C# and Python trace lines (this file must stay ASCII
        # in code: Windows PowerShell 5.1 reads a script without a byte-order mark as ANSI).
        $dot = '  ' + [char]0xB7 + ' '
        foreach ($line in $lines) { $known[$Script + '|' + $line] = $leaf + ':' + $line + $dot + $source[$line - 1].Trim() }
        # $_ is the breakpoint that was hit. No 'break': the script never pauses.
        $action = {
            try {
                $text = $global:TlachihuaniScriptLines[$_.Script + '|' + $_.Line]
                $global:TlachihuaniScriptLineWriter.WriteLine((Get-Date -Format 'HH:mm:ss.ffffff') + '  ' + $text)
            } catch { }
        }
        foreach ($line in $lines) { [void](Set-PSBreakpoint -Script $Script -Line $line -Action $action) }
        $writer.WriteLine((Get-Date -Format 'HH:mm:ss.ffffff') + '  tracing ' + $lines.Count + ' statement lines of ' + $Script)
    } catch {
        Write-Warning ('The script log could not be started: ' + $_.Exception.Message)
    }
}
