#requires -Version 7.0
$ErrorActionPreference = 'Stop'
$root = Join-Path ([IO.Path]::GetTempPath()) ('aigenterra-hook-capture-check-' + [Guid]::NewGuid().ToString('N'))
$capture = Join-Path $PSScriptRoot 'Capture-Hook.ps1'
[IO.Directory]::CreateDirectory($root) | Out-Null
$expected = [Text.Encoding]::UTF8.GetBytes("{`"text`":`"é한😀`"}`r`n")
function Invoke-Capture([string] $Directory) {
    $start = [Diagnostics.ProcessStartInfo]::new('pwsh.exe')
    $start.UseShellExecute = $false; $start.CreateNoWindow = $true
    $start.RedirectStandardInput = $true; $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
    foreach ($arg in @('-NoProfile', '-File', $capture, '-Directory', $Directory)) { $start.ArgumentList.Add($arg) }
    $child = [Diagnostics.Process]::Start($start)
    $child.StandardInput.BaseStream.Write($expected); $child.StandardInput.Close()
    $output = $child.StandardOutput.ReadToEnd(); $errors = $child.StandardError.ReadToEnd()
    $child.WaitForExit()
    if ($child.ExitCode -ne 0 -or $output.Length -ne 0 -or $errors.Length -ne 0) { throw 'Capture is not silent/fail-open.' }
    $child.Dispose()
}
$destination = Join-Path $root 'path with spaces'
Invoke-Capture $destination
$received = [IO.File]::ReadAllBytes((Get-ChildItem $destination -Filter '*.bin').FullName)
if ([Convert]::ToHexString($received) -ne [Convert]::ToHexString($expected)) { throw 'Bytes changed.' }
$blocked = Join-Path $root 'file-not-directory'
[IO.File]::WriteAllText($blocked, 'occupied')
Invoke-Capture $blocked
Write-Output 'PASS: exact UTF-8/CRLF bytes, paths with spaces, silent success and silent storage failure.'
