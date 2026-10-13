#requires -Version 7.0
param(
    [Parameter(Mandatory)][string] $CodexExecutable,
    [Parameter(Mandatory)][string] $CodexHome,
    [Parameter(Mandatory)][string] $ThreadId,
    [Parameter(Mandatory)][string] $EvidenceDirectory,
    [string] $PgBin = 'C:/Program Files/PostgreSQL/17/bin',
    [int] $PgPort = 57641
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
New-Item -ItemType Directory -Force -Path $evidence | Out-Null
$database = 'test_reconciliation_' + [Guid]::NewGuid().ToString('N')
$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
$listener.Start()
$origin = 'http://127.0.0.1:' + $listener.LocalEndpoint.Port
$listener.Stop()
$namespaceId = [Guid]::NewGuid().ToString()
$sourceId = [Guid]::NewGuid().ToString()
$enrollment = Join-Path $evidence 'enrollment.json'
@{ version=1; runtimeNamespaceId=$namespaceId; sourceId=$sourceId; executable=$CodexExecutable;
   codexHome=$CodexHome; apiBaseUrl=$origin; stateDirectory=(Join-Path $evidence 'state'); hookSourceIds=@(); threadIds=@($ThreadId) } |
    ConvertTo-Json | Set-Content -LiteralPath $enrollment
$apiDll = Join-Path $root 'src/Aigenterra.Api/bin/Debug/net10.0/Aigenterra.Api.dll'
$collectorDll = Join-Path $root 'src/Aigenterra.Collector/bin/Debug/net10.0/Aigenterra.Collector.dll'
$api = $null; $watcher = $null; $sequence = 0
function Start-TestProcess([string] $dll, [string[]] $arguments, [string] $name) {
    $start = [Diagnostics.ProcessStartInfo]::new('dotnet')
    $start.UseShellExecute = $false; $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
    $start.ArgumentList.Add($dll)
    foreach ($argument in $arguments) { $start.ArgumentList.Add($argument) }
    $start.Environment['ConnectionStrings__aigenterra'] = "Host=127.0.0.1;Port=$PgPort;Database=$database;Username=postgres;Pooling=false"
    $start.Environment['Database__ApplyMigrationsOnStartup'] = 'true'
    $start.Environment['Runtime__NormalizeCaptures'] = 'true'
    $start.Environment['AIGENTERRA_SPOOL_PATH'] = Join-Path $evidence 'spool'
    $start.Environment['ASPNETCORE_URLS'] = $origin
    $process = [Diagnostics.Process]::Start($start)
    $outFile = [IO.File]::Create((Join-Path $evidence "$name.stdout"))
    $errFile = [IO.File]::Create((Join-Path $evidence "$name.stderr"))
    return @{ Process=$process; Out=$outFile; Err=$errFile;
        OutTask=$process.StandardOutput.BaseStream.CopyToAsync($outFile); ErrTask=$process.StandardError.BaseStream.CopyToAsync($errFile) }
}
function Stop-TestProcess($running) {
    if ($null -eq $running) { return }
    if (!$running.Process.HasExited) { $running.Process.Kill($false); $running.Process.WaitForExit(5000) | Out-Null }
    $null = $running.OutTask.GetAwaiter().GetResult(); $null = $running.ErrTask.GetAwaiter().GetResult()
    $running.Out.Dispose(); $running.Err.Dispose(); $running.Process.Dispose()
}
function Wait-Condition([scriptblock] $condition) {
    $until = [DateTime]::UtcNow.AddSeconds(30)
    while ([DateTime]::UtcNow -lt $until) {
        try { if (& $condition) { return } } catch { }
        Start-Sleep -Milliseconds 250
    }
    throw 'Isolated reconciliation condition timed out; inspect retained logs.'
}
try {
    & (Join-Path $PgBin 'createdb.exe') -h 127.0.0.1 -p $PgPort -U postgres $database
    if ($LASTEXITCODE) { throw 'Could not create disposable test database.' }
    $api = Start-TestProcess $apiDll @() 'api-1'
    Wait-Condition { (Invoke-RestMethod "$origin/status").status -eq 'ok' }
    $watcher = Start-TestProcess $collectorDll @('codex','reconcile','--enrollment',$enrollment,'--watch') 'watch-1'
    $healthUrl = "$origin/runtime/sources/$sourceId/reconciliation?runtimeNamespaceId=$namespaceId"
    $targetsUrl = "$origin/runtime/reconciliation/targets?runtimeNamespaceId=$namespaceId&sourceId=$sourceId"
    Wait-Condition { (Invoke-RestMethod $healthUrl).readerState -eq 'history_read_partial_coverage' }
    $targets = Invoke-RestMethod $targetsUrl
    if ($targets.targets.Count -ne 1 -or $targets.targets[0].threadId -ne $ThreadId) { throw 'Unexpected target scope.' }
    $before = (Invoke-RestMethod $healthUrl).gapCount
    Stop-TestProcess $api; $api = $null
    Start-Sleep -Seconds 7
    $api = Start-TestProcess $apiDll @() 'api-2'
    Wait-Condition { (Invoke-RestMethod $healthUrl).recentGaps.input.outcome -contains 'target_discovery_unavailable' -or
        (Invoke-RestMethod $healthUrl).recentGaps.input.outcome -contains 'archive_unavailable' }
    Stop-TestProcess $watcher; $watcher = $null
    $watcher = Start-TestProcess $collectorDll @('codex','reconcile','--enrollment',$enrollment,'--watch') 'watch-2'
    Wait-Condition { (Invoke-RestMethod $healthUrl).gapCount -gt $before + 1 }
    $health = Invoke-RestMethod $healthUrl
    if ($health.gate -ne 'KEEP_GATE_BLOCKED' -or $health.ownerState -ne 'Unknown') { throw 'Invalid source-health claim.' }
    @{ result='PASS'; gate=$health.gate; targetCount=$targets.targets.Count; gaps=$health.gapCount;
       checks=@('scoped actual history read','API outage recovery','reader restart','persistent gaps','unknown owner state') } |
        ConvertTo-Json | Tee-Object -FilePath (Join-Path $evidence 'result.json')
} finally {
    Stop-TestProcess $watcher
    Stop-TestProcess $api
    & (Join-Path $PgBin 'dropdb.exe') -h 127.0.0.1 -p $PgPort -U postgres --force $database
}
