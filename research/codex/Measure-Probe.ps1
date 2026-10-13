#requires -Version 7.0
param([Parameter(Mandatory)][string] $Workspace, [Parameter(Mandatory)][string] $Evidence)
$ErrorActionPreference = 'Stop'
$summary = Get-Content -LiteralPath (Join-Path $Evidence 'summary.json') -Raw | ConvertFrom-Json -Depth 100
$wire = Join-Path $Evidence 'received.ndjson'
$lines = [IO.File]::ReadAllLines($wire)
$messages = @($lines | ForEach-Object { $_ | ConvertFrom-Json -Depth 100 })
$receipts = @(Get-Content -LiteralPath (Join-Path $Evidence 'receipt-times.ndjson') | ForEach-Object { $_ | ConvertFrom-Json })
$seconds = ($receipts[-1].ElapsedMs - $receipts[0].ElapsedMs) / 1000
$utf8 = [Text.Encoding]::UTF8
$lineBytes = @($lines | ForEach-Object { $utf8.GetByteCount($_) })
$eventBytes = for ($i = 0; $i -lt $messages.Count; $i++) { if ($messages[$i].method) { $lineBytes[$i] } }
$results = @($messages | Where-Object { $_.method -eq 'item/completed' -and $_.params.item.type -eq 'mcpToolCall' } | ForEach-Object { $utf8.GetByteCount(($_.params.item.result | ConvertTo-Json -Depth 100 -Compress)) })
$hookRuntimeDurations = @($messages | Where-Object { $_.method -eq 'hook/completed' -and $null -ne $_.params.run.durationMs } | ForEach-Object { $_.params.run.durationMs } | Sort-Object)
$durations = @(Get-ChildItem -LiteralPath (Join-Path $Workspace 'evidence\hooks') -Filter '*.bin' | ForEach-Object {
    $hook = Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json -Depth 100
    if ($hook.session_id -eq $summary.threadStart.result.thread.id) {
        $meta = Get-Content -LiteralPath ([IO.Path]::ChangeExtension($_.FullName, '.meta.json')) -Raw | ConvertFrom-Json
        $meta.captureDurationMs
    }
} | Sort-Object)
@{
    scenario = $summary.scenario
    runtime = $summary.initialize.result.userAgent
    messages = $messages.Count
    rawBytes = (Get-Item -LiteralPath $wire).Length
    receiveWindowSeconds = [math]::Round($seconds, 3)
    messagesPerSecond = if ($seconds -gt 0) { [math]::Round($messages.Count / $seconds, 2) } else { $null }
    rawBytesPerSecond = if ($seconds -gt 0) { [math]::Round((Get-Item -LiteralPath $wire).Length / $seconds, 2) } else { $null }
    peakMessagesPerOneSecondBucket = ($receipts | Group-Object { [math]::Floor($_.ElapsedMs / 1000) } | Measure-Object Count -Maximum).Maximum
    largestFrameBytesWithoutDelimiter = ($lineBytes | Measure-Object -Maximum).Maximum
    largestNotificationBytesWithoutDelimiter = ($eventBytes | Measure-Object -Maximum).Maximum
    largestMcpResultReencodedBytes = ($results | Measure-Object -Maximum).Maximum
    hookSamples = $durations.Count
    hookCaptureMedianMs = if ($durations.Count) { $durations[[math]::Floor(($durations.Count - 1) * 0.5)] } else { $null }
    hookCaptureP95Ms = if ($durations.Count) { $durations[[math]::Ceiling(($durations.Count - 1) * 0.95)] } else { $null }
    hookCaptureMaxMs = ($durations | Measure-Object -Maximum).Maximum
    hookRuntimeMedianMs = if ($hookRuntimeDurations.Count) { $hookRuntimeDurations[[math]::Floor(($hookRuntimeDurations.Count - 1) * 0.5)] } else { $null }
    hookRuntimeMaxMs = ($hookRuntimeDurations | Measure-Object -Maximum).Maximum
    limitations = 'Small synthetic sample, not a load test. Hook capture excludes PowerShell startup. Result bytes are JSON re-encoding, not original payload slices. Collector queue depth was not measured.'
} | ConvertTo-Json -Depth 8
