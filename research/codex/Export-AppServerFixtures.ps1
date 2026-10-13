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
$threadIds = @($threadId) + @($selected.params.threadId) + @($selected.params.item.receiverThreadIds)
$hooks = @(Get-ChildItem -LiteralPath (Join-Path $Workspace 'evidence\hooks') -Filter '*.bin' | ForEach-Object {
        $payload = Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json -Depth 100
        if ($payload.session_id -in $threadIds) { $payload }
    })
$ids = @{}
# One sanitization pass across BOTH streams preserves explicit equality between IDs.
$json = @{ appServer = $selected; hooks = $hooks } | ConvertTo-Json -Depth 100
$json = $json.Replace($Workspace.Replace('\','\\'),'C:\\probe').Replace($Workspace,'C:\probe')
$json = $json.Replace($env:USERPROFILE.Replace('\','\\'),'C:\\user').Replace($env:USERPROFILE,'C:\user')
$json = [regex]::Replace($json, '(?i)[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}', {
        param($match)
        if (-not $ids.ContainsKey($match.Value)) { $ids[$match.Value] = '00000000-0000-4000-8000-' + ($ids.Count + 1).ToString('D12') }
        $ids[$match.Value]
    })
[IO.Directory]::CreateDirectory($Destination) | Out-Null
[IO.File]::WriteAllText((Join-Path $Destination 'events.json'), $json)
@{ userAgent = $summary.initialize.result.userAgent; model = $summary.model; turnStatus = $summary.turnStatus; runStatus = $summary.status; sourceSha256 = (Get-FileHash -LiteralPath (Join-Path $Evidence 'received.ndjson')).Hash; sourceBytes = (Get-Item -LiteralPath (Join-Path $Evidence 'received.ndjson')).Length; totalMessages = $messages.Count; selectedMessages = $selected.Count; selectedHooks = $hooks.Count; stages = @('AppServerVisible','HookVisible'); sanitized = $true; note = 'Jointly pseudonymized subset. Account, reasoning, token deltas and unrelated response payloads are deliberately omitted. Original stream is retained locally.' } |
    ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $Destination 'manifest.json') -Encoding utf8NoBOM
Write-Output "Exported $($selected.Count) protocol messages and $($hooks.Count) matching hooks. Review strings before commit."
