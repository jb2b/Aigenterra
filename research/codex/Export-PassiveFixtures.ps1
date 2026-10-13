#requires -Version 7.0
param(
    [Parameter(Mandatory)][string] $Archive,
    [Parameter(Mandatory)][ValidateSet('cli','desktop')][string] $Surface,
    [Parameter(Mandatory)][string] $Destination
)
$ErrorActionPreference = 'Stop'
if (Test-Path -LiteralPath $Destination) { throw 'Destination must be new; never overwrite compatibility fixtures.' }
function Read-Json([string] $Path) { Get-Content -Raw -LiteralPath $Path | ConvertFrom-Json -AsHashtable -Depth 100 }
function Select-Fields($Value, [string[]] $Names) {
    $selected = [ordered]@{}
    foreach ($name in $Names) { if ($Value.Contains($name)) { $selected[$name] = $Value[$name] } }
    return $selected
}
function Select-Item($Item) {
    $selected = Select-Fields $Item @('type','id','server','tool','status','error','duration','arguments','agentThreadId','agentPath','kind','model','reasoningEffort','senderThreadId','receiverThreadIds')
    if ($Item.type -eq 'agentMessage') {
        $selected.text = if ($Item.text -match '^(CLI_|CHILD_|DESKTOP_|NEW_CHILD_)') { $Item.text } else { '[model prose omitted]' }
    }
    if ($Item.type -eq 'mcpToolCall' -and $Item.result) {
        if ($Item.server -eq 'observability_probe' -or $Item.status -eq 'failed') { $selected.result = $Item.result }
        else { $selected.resultOmitted = 'Public documentation lookup text omitted; status retained.' }
    }
    return $selected
}
function Select-Turn($Turn) {
    $selected = Select-Fields $Turn @('id','status','rootTurnId','itemsView')
    if ($Turn.Contains('items')) {
        $selected.items = @($Turn.items | Where-Object { $_.type -in @('agentMessage','mcpToolCall','subAgentActivity','collabAgentToolCall') } | ForEach-Object { Select-Item $_ })
    }
    return $selected
}
function Read-Connection([string] $Directory) {
    $summary = Read-Json (Join-Path $Directory 'summary.json')
    $sent = @(Get-Content -LiteralPath (Join-Path $Directory 'sent.ndjson') | ForEach-Object { $_ | ConvertFrom-Json -AsHashtable -Depth 100 })
    $messages = @(Get-Content -LiteralPath (Join-Path $Directory 'received.ndjson') | ForEach-Object { $_ | ConvertFrom-Json -AsHashtable -Depth 100 })
    $events = @($messages | Where-Object { $_.method -in @('thread/status/changed','turn/started','turn/completed','item/started','item/completed') } | ForEach-Object {
        $event = Select-Fields $_ @('method','emittedAtMs')
        $event.params = Select-Fields $_.params @('threadId','turnId','status')
        if ($_.params['item']) { $event.params['item'] = Select-Item $_.params['item'] }
        if ($_.params.turn) { $event.params.turn = Select-Turn $_.params.turn }
        $event
    })
    $snapshots = @($messages | Where-Object { $_.id -in @(2,3,4,5,6,7) } | ForEach-Object {
        $entry = [ordered]@{ id = $_.id; method = ($sent | Where-Object id -eq $_.id).method }
        if ($_.error) { $entry.error = $_.error }
        elseif ($entry.method -eq 'thread/loaded/list') { $entry.result = $_.result }
        elseif ($entry.method -eq 'thread/read') { $entry.result = @{ thread = (Select-Fields $_.result.thread @('id','source','originator','status')) } }
        else { $entry.result = @{ data = @($_.result.data | ForEach-Object { Select-Turn $_ }); nextCursor = $_.result.nextCursor } }
        $entry
    })
    return [ordered]@{
        summary = (Select-Fields $summary @('transport','threadId','startedAt','finishedAt','failure','messageCount','serverRequests'))
        notificationCounts = @($messages | Where-Object method | Group-Object method | ForEach-Object { @{ method = $_.Name; count = $_.Count } })
        sent = $sent
        events = $events
        snapshots = $snapshots
        sourceSha256 = (Get-FileHash -LiteralPath (Join-Path $Directory 'received.ndjson')).Hash
    }
}
$version = if ($Surface -eq 'cli') { '0.162.1' } else { '0.160.1' }
$base = Join-Path $Archive $(if ($Surface -eq 'cli') { 'cli-evidence' } else { 'actual-desktop' })
$names = if ($Surface -eq 'cli') { @('unix-1','unix-2','child-history') } else { @('history-1','history-2','child-history') }
$connections = [ordered]@{}
foreach ($name in $names) { $connections[$name] = Read-Connection (Join-Path $base $name) }
$external = @()
if ($Surface -eq 'cli') {
    $external = @(Get-ChildItem -LiteralPath (Join-Path $base 'mcp') -Filter '*.responses.ndjson' | ForEach-Object {
        Get-Content -LiteralPath $_.FullName | ForEach-Object {
            $message = $_ | ConvertFrom-Json -AsHashtable -Depth 100
            if ($message.result.structuredContent.marker) { $message }
        }
    })
}
$fixture = [ordered]@{
    fixtureVersion = 1
    scenario = 'passive-revalidation-20261013'
    runtimeVersion = $version
    surface = $Surface
    classification = $(if ($Surface -eq 'cli') { 'UNKNOWN' } else { 'PASSIVE_HISTORY_ONLY' })
    classificationScope = 'Required complete passive runtime observation, including neutrality; partial live delivery is itemized separately.'
    gate = 'KEEP_GATE_BLOCKED'
    connections = $connections
    externalMcp = $external
}
$json = $fixture | ConvertTo-Json -Depth 100
$ids = @{}
$json = [regex]::Replace($json, '(?i)[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}', {
    param($match)
    if (-not $ids.ContainsKey($match.Value)) { $ids[$match.Value] = '00000000-0000-4000-8000-' + ($ids.Count + 1).ToString('D12') }
    $ids[$match.Value]
})
$json = [regex]::Replace($json, '\b(?:msg|call|ctc)_[0-9a-f]{16,}\b', {
    param($match)
    if (-not $ids.ContainsKey($match.Value)) { $ids[$match.Value] = 'synthetic-item-' + ($ids.Count + 1).ToString('D4') }
    $ids[$match.Value]
})
$json = $json.Replace($env:USERPROFILE.Replace('\','\\'), 'C:\\user').Replace($env:USERPROFILE, 'C:\user')
[IO.Directory]::CreateDirectory($Destination) | Out-Null
[IO.File]::WriteAllText((Join-Path $Destination 'events.json'), $json)
$binary = Read-Json (Join-Path $Archive "$Surface-current\executable.json")
$hashes = Read-Json (Join-Path $Archive 'schema-hashes.json')
@{
    runtimeVersion = $version
    executableSha256 = $binary.sha256
    schemaHashes = @($hashes | Where-Object { $_.bundle -like "$version-*" -and $_.bundle -notlike '*stable' })
    sanitized = $true
    note = 'Allowlisted subset; joined pseudonyms across connections/history/MCP. No account, reasoning, user instruction bodies, paths, or public lookup result text. Originals remain in the private archive.'
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $Destination 'manifest.json') -Encoding utf8NoBOM
Write-Output "Exported $Surface $version fixtures to a new destination; review before commit."
