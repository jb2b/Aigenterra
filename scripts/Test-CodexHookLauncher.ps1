#requires -Version 7.0
# Tests the installed command through an outer PowerShell, as Codex executes it.
param([Parameter(Mandatory)][string] $RepositoryPath)
$ErrorActionPreference = 'Stop'
$configuration = Get-Content (Join-Path $RepositoryPath '.codex/hooks.json') -Raw | ConvertFrom-Json
$command = @($configuration.hooks.SessionStart.hooks | Where-Object {
    $_.commandWindows -match 'invoke-aigenterra-collector\.ps1'
})[0].commandWindows
if (-not $command) { throw 'Install the current Aigenterra hooks first.' }
$spoolPath = Join-Path ([IO.Path]::GetTempPath()) ('aigenterra-launcher-' + [Guid]::NewGuid().ToString('N'))
$payload = [Text.Encoding]::UTF8.GetBytes("{`"hook_event_name`":`"SessionStart`",`"extra`":`"café`"}`r`n")
$start = [Diagnostics.ProcessStartInfo]::new((Get-Command pwsh).Source)
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.RedirectStandardInput = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$start.Environment['AIGENTERRA_SPOOL_PATH'] = $spoolPath
foreach ($argument in @('-NoProfile', '-Command', $command)) { $start.ArgumentList.Add($argument) }
$process = [Diagnostics.Process]::Start($start)
try {
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    $process.StandardInput.BaseStream.Write($payload)
    $process.StandardInput.Close()
    if (-not $process.WaitForExit(10000)) { $process.Kill($true); throw 'Launcher timed out.' }
    if ($process.ExitCode -ne 0 -or $stdout.Result.Trim() -ne '{}' -or $stderr.Result) {
        throw 'Launcher failed; no payload or stderr is logged.'
    }
    $records = @(Get-ChildItem (Join-Path $spoolPath 'pending') -Directory)
    if ($records.Count -ne 1) { throw 'Expected exactly one durable record.' }
    $saved = [IO.File]::ReadAllBytes((Join-Path $records[0].FullName 'payload.bin'))
    if ([Convert]::ToBase64String($saved) -cne [Convert]::ToBase64String($payload)) {
        throw 'Launcher changed the input bytes.'
    }
    Write-Output "PASS: outer-shell launch, durable capture, exact UTF-8/CRLF bytes. Evidence: $spoolPath"
} finally { $process.Dispose() }
