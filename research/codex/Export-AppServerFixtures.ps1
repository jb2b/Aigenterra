#requires -Version 7.0
param([Parameter(Mandatory)][string] $Workspace, [Parameter(Mandatory)][string] $Evidence, [Parameter(Mandatory)][string] $Destination)
$ErrorActionPreference = 'Stop'
$Workspace = (Resolve-Path -LiteralPath $Workspace).Path
$Evidence = (Resolve-Path -LiteralPath $Evidence).Path
if (Test-Path -LiteralPath $Destination) { throw 'Fixture destination must be new.' }
$summary = Get-Content -LiteralPath (Join-Path $Evidence 'summary.json') -Raw | ConvertFrom-Json -Depth 100
$threadId = $summary.threadStart.result.thread.id
$messages = @(Get-Content -LiteralPath (Join-Path $Evidence 'received.ndjson') | ForEach-Object { $_ | ConvertFrom-Json -Depth 100 })
$selected = @($messages | Where-Object {
        ($_.id -eq 1 -and $_.result.userAgent) -or
        ($_.method -in @('thread/started','thread/status/changed','thread/tokenUsage/updated','turn/started','turn/completed','hook/started','hook/completed','mcpServer/startupStatus/updated','deprecationNotice','warning','configWarning')) -or
        ($_.method -in @('item/started','item/completed') -and $_.params.item.type -ne 'reasoning')
    })
$threadIds = @($threadId) + @($selected | ForEach-Object { $_.params.threadId; $_.params.item.receiverThreadIds; $_.params.item.agentThreadId })
$hooks = @(Get-ChildItem -LiteralPath (Join-Path $Workspace 'evidence\hooks') -Filter '*.bin' | ForEach-Object {
        $payload = Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json -Depth 100
        if ($payload.session_id -in $threadIds) { $payload }
    })
$markers = @($selected | ForEach-Object { $_.params.item.arguments.marker } | Where-Object { $null -ne $_ })
$externalMcp = @()
$modelToolCalls = @()
if ($summary.scenario -eq 'code-mode') {
    $modelToolCalls = @(Get-Content -LiteralPath $summary.threadStart.result.thread.path | ForEach-Object {
        $entry = $_ | ConvertFrom-Json -Depth 100
        if ($entry.type -eq 'response_item' -and $entry.payload.type -in @('function_call','custom_tool_call')) { $entry.payload }
    })
}
$mcpPath = Join-Path $Workspace 'evidence\mcp'
if (Test-Path -LiteralPath $mcpPath) {
    $externalMcp = @(Get-ChildItem -LiteralPath $mcpPath -Filter '*.responses.ndjson' | ForEach-Object {
            Get-Content -LiteralPath $_.FullName | ForEach-Object {
                $response = $_ | ConvertFrom-Json -Depth 100
                if ($response.result.structuredContent.marker -in $markers -and $null -ne $response.result.structuredContent.marker) { $response }
            }
        })
}
$ids = @{}
# One sanitization pass across BOTH streams preserves explicit equality between IDs.
$json = @{ appServer = $selected; hooks = $hooks; externalMcp = $externalMcp; modelToolCalls = $modelToolCalls; diagnostics = $summary.scenarioEvidence } | ConvertTo-Json -Depth 100
# Early probe summaries called the second resume 'offline', but unsubscribe
# demonstrably did NOT unload the owner. Correct the diagnostic label only.
$json = $json.Replace('offlineResumeSucceeded','resumeAfterUnsubscribeSucceeded').Replace('offlineResumeError','resumeAfterUnsubscribeError').Replace('loadedAfterOfflineResume','loadedAfterSecondResume')
$json = $json.Replace($Workspace.Replace('\','\\'),'C:\\probe').Replace($Workspace,'C:\probe')
$json = $json.Replace($env:USERPROFILE.Replace('\','\\'),'C:\\user').Replace($env:USERPROFILE,'C:\user')
$json = [regex]::Replace($json, '(?i)[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}', {
        param($match)
        if (-not $ids.ContainsKey($match.Value)) { $ids[$match.Value] = '00000000-0000-4000-8000-' + ($ids.Count + 1).ToString('D12') }
        $ids[$match.Value]
    })
[IO.Directory]::CreateDirectory($Destination) | Out-Null
[IO.File]::WriteAllText((Join-Path $Destination 'events.json'), $json)
$stages = @('AppServerVisible','HookVisible')
if ($externalMcp.Count) { $stages += 'ExternalRaw' }
if ($modelToolCalls.Count) { $stages += 'RuntimeTranscriptVisible' }
@{ userAgent = $summary.initialize.result.userAgent; model = $summary.model; turnStatus = $summary.turnStatus; runStatus = $summary.status; sourceSha256 = (Get-FileHash -LiteralPath (Join-Path $Evidence 'received.ndjson')).Hash; sourceBytes = (Get-Item -LiteralPath (Join-Path $Evidence 'received.ndjson')).Length; totalMessages = $messages.Count; selectedMessages = $selected.Count; selectedHooks = $hooks.Count; externalResults = $externalMcp.Count; stages = $stages; sanitized = $true; note = 'Jointly pseudonymized subset. Account, reasoning, token deltas and unrelated response payloads are deliberately omitted. Original stream is retained locally.' } |
    ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $Destination 'manifest.json') -Encoding utf8NoBOM
Write-Output "Exported $($selected.Count) protocol messages and $($hooks.Count) matching hooks. Review strings before commit."
