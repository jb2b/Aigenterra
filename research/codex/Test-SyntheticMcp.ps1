#requires -Version 7.0
$ErrorActionPreference = 'Stop'
$root = Join-Path ([IO.Path]::GetTempPath()) ('aigenterra-mcp-check-' + [Guid]::NewGuid().ToString('N'))
$start = [Diagnostics.ProcessStartInfo]::new('pwsh.exe')
$start.UseShellExecute = $false; $start.CreateNoWindow = $true
$start.RedirectStandardInput = $true; $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
foreach ($arg in @('-NoProfile','-File',(Join-Path $PSScriptRoot 'Synthetic-Mcp.ps1'),'-EvidenceDirectory',$root)) { $start.ArgumentList.Add($arg) }
$process = [Diagnostics.Process]::Start($start)
$output = [IO.MemoryStream]::new()
$copy = $process.StandardOutput.BaseStream.CopyToAsync($output)
$errors = $process.StandardError.ReadToEndAsync()
foreach ($request in @(
        @{ jsonrpc='2.0'; id=1; method='initialize'; params=@{protocolVersion='2025-03-26'} },
        @{ jsonrpc='2.0'; id=2; method='tools/call'; params=@{name='echo';arguments=@{marker='unit';size=8}} },
        @{ jsonrpc='2.0'; id=3; method='tools/call'; params=@{name='fail';arguments=@{marker='unit-error'}} }
    )) { $process.StandardInput.WriteLine(($request | ConvertTo-Json -Depth 10 -Compress)) }
$process.StandardInput.Close()
if (-not $process.WaitForExit(10000)) { $process.Kill($true); throw 'MCP server timeout.' }
$copy.GetAwaiter().GetResult() | Out-Null
if ($process.ExitCode -ne 0 -or $errors.GetAwaiter().GetResult().Length -ne 0) { throw 'MCP server failed.' }
$actual = $output.ToArray()
$recorded = [IO.File]::ReadAllBytes((Get-ChildItem -LiteralPath $root -Filter '*.responses.ndjson').FullName)
if ([Convert]::ToHexString($actual) -ne [Convert]::ToHexString($recorded)) { throw 'External raw evidence differs from emitted bytes.' }
$responses = [Text.Encoding]::UTF8.GetString($actual).Split("`n",[StringSplitOptions]::RemoveEmptyEntries) | ForEach-Object { $_ | ConvertFrom-Json -Depth 20 }
if ($responses[1].result.isError -or -not $responses[2].result.isError -or $responses[1].result.structuredContent.padding.Length -ne 8) { throw 'Incorrect synthetic result/error.' }
$process.Dispose(); $output.Dispose()
Write-Output 'PASS: external response bytes match stdout exactly; bounded success and isError results verified.'
