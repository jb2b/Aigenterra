#requires -Version 7.0
param([Parameter(Mandatory)][string] $Directory)

# Research-only observer. Never writes model-visible output or makes permission decisions.
$ErrorActionPreference = 'Stop'
$timer = [Diagnostics.Stopwatch]::StartNew()
try {
    [IO.Directory]::CreateDirectory($Directory) | Out-Null
    $id = [Guid]::NewGuid().ToString('N')
    $receivedAt = [DateTimeOffset]::UtcNow
    $file = [IO.File]::Open((Join-Path $Directory "$id.bin"), [IO.FileMode]::CreateNew)
    try { [Console]::OpenStandardInput().CopyTo($file); $file.Flush($true); $length = $file.Length }
    finally { $file.Dispose() }
    @{ receivedAt = $receivedAt.ToString('O'); bytes = $length; captureDurationMs = $timer.Elapsed.TotalMilliseconds } |
        ConvertTo-Json -Compress | Set-Content -LiteralPath (Join-Path $Directory "$id.meta.json") -Encoding utf8NoBOM
} catch {
    # Deliberately fail open, even when storage is unavailable.
}
exit 0
