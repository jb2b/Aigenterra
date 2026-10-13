#requires -Version 7.0
param([Parameter(Mandatory)][string] $ProbeRoot, [Parameter(Mandatory)][string] $Destination)
$ErrorActionPreference = 'Stop'
$ProbeRoot = (Resolve-Path -LiteralPath $ProbeRoot).Path
if (Test-Path -LiteralPath $Destination) { throw 'Choose a new fixture destination; existing fixtures are immutable.' }
[IO.Directory]::CreateDirectory($Destination) | Out-Null
$ids = @{}
$counts = @{}
$manifest = [Collections.Generic.List[object]]::new()
$files = Get-ChildItem -LiteralPath (Join-Path $ProbeRoot 'evidence\hooks') -Filter '*.bin' |
    Sort-Object { (Get-Content -LiteralPath ($_.FullName -replace '\.bin$', '.meta.json') -Raw | ConvertFrom-Json).receivedAt }
foreach ($file in $files) {
    $raw = [IO.File]::ReadAllText($file.FullName)
    $payload = $raw | ConvertFrom-Json -Depth 100
    $eventName = $payload.hook_event_name
    $counts[$eventName] = 1 + $counts[$eventName]
    # Source bytes remain untouched. These are explicitly sanitized JSON copies.
    $sanitized = $raw.Replace($ProbeRoot.Replace('\', '\\'), 'C:\\probe').Replace($ProbeRoot, 'C:\probe')
    $sanitized = $sanitized.Replace($env:USERPROFILE.Replace('\', '\\'), 'C:\\user').Replace($env:USERPROFILE, 'C:\user')
    $sanitized = [regex]::Replace($sanitized, '(?i)[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}', {
            param($match)
            if (-not $ids.ContainsKey($match.Value)) { $ids[$match.Value] = '00000000-0000-4000-8000-' + ($ids.Count + 1).ToString('D12') }
            $ids[$match.Value]
        })
    $parsed = $sanitized | ConvertFrom-Json -Depth 100
    $name = "$eventName-$($counts[$eventName]).json"
    $parsed | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath (Join-Path $Destination $name) -Encoding utf8NoBOM
    $manifest.Add(@{ file = $name; sourceSha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash; sourceBytes = $file.Length; stage = 'HookVisible'; sanitized = $true })
}
$probe = Get-Content -LiteralPath (Join-Path $ProbeRoot 'manifest.json') -Raw | ConvertFrom-Json
@{ binaryVersion = $probe.binaryVersion; model = $probe.model; codeMode = $probe.codeMode; exitCode = $probe.exitCode; timedOut = $probe.timedOut; elapsedMs = $probe.elapsedMs; inventory = $probe.inventory; files = @($manifest); note = 'Sanitized copies; original bytes are retained locally. Ordering here is collector receipt order, not causality.' } |
    ConvertTo-Json -Depth 20 | Set-Content -LiteralPath (Join-Path $Destination 'manifest.json') -Encoding utf8NoBOM
Write-Output "Exported $($files.Count) sanitized hook payloads; review before committing."
