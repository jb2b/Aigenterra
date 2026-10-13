#requires -Version 7.0
param(
    [Parameter(Mandatory)][Guid] $SessionId,
    [Parameter(Mandatory)][Guid] $TurnId,
    [string] $IngestUrl = $env:AIGENTERRA_INGEST_URL,
    [switch] $InspectOnly
)

# Explicit, bounded runtime-side capture. Does not start/resume turns or execute their commands.
# No transcripts, response output, credentials or user messages are sent to Aigenterra.
$ErrorActionPreference = 'Stop'
$captureProcess = $null
$client = $null
try {
    if ($SessionId -eq [Guid]::Empty -or $TurnId -eq [Guid]::Empty) { throw 'IDs are required.' }
    $codexPath = (Get-Command codex -ErrorAction Stop).Source
    $start = [Diagnostics.ProcessStartInfo]::new()
    if ($codexPath.EndsWith('.ps1')) {
        $start.FileName = (Get-Process -Id $PID).Path
        foreach ($arg in @('-NoProfile', '-File', $codexPath, 'app-server')) { $start.ArgumentList.Add($arg) }
    } else {
        $start.FileName = $codexPath
        $start.ArgumentList.Add('app-server')
    }
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardInput = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $captureProcess = [Diagnostics.Process]::Start($start)
    # Drain stderr without logging runtime-local details or blocking the child.
    $stderrTask = $captureProcess.StandardError.BaseStream.CopyToAsync([IO.Stream]::Null)
    $deadline = [Threading.CancellationTokenSource]::new([TimeSpan]::FromSeconds(20))
    $receivedChars = 0
    function Send-Rpc($message) {
        $captureProcess.StandardInput.WriteLine(($message | ConvertTo-Json -Depth 12 -Compress))
        $captureProcess.StandardInput.Flush()
    }
    function Read-Rpc([int] $responseId) {
        while ($true) {
            # Reject oversized histories before JSON interpretation; ReadLineAsync still buffers the line.
            $lineTask = $captureProcess.StandardOutput.ReadLineAsync($deadline.Token)
            $line = $lineTask.AsTask().GetAwaiter().GetResult()
            if ($null -eq $line) { throw 'Runtime closed the read channel.' }
            $script:receivedChars += $line.Length
            if ($script:receivedChars -gt 16777216) { throw 'Runtime history exceeds capture limit.' }
            $message = $line | ConvertFrom-Json -Depth 100
            if ($message.id -eq $responseId) {
                if ($null -ne $message.error) { throw 'Runtime rejected read request.' }
                return $message.result
            }
        }
    }
    Send-Rpc @{ id = 1; method = 'initialize'; params = @{ clientInfo = @{ name = 'aigenterra_capture'; version = '1' }; capabilities = @{ experimentalApi = $true } } }
    $initial = Read-Rpc 1
    Send-Rpc @{ method = 'initialized' }
    Send-Rpc @{ id = 2; method = 'thread/read'; params = @{ threadId = $SessionId.ToString(); includeTurns = $true } }
    $result = Read-Rpc 2
    if ($result.thread.id -ne $SessionId.ToString()) { throw 'Runtime returned a different session.' }
    $turn = @($result.thread.turns | Where-Object id -eq $TurnId.ToString())
    if ($turn.Count -ne 1) { throw 'Requested turn was not found.' }
    $runtimeVersion = [string]$initial.userAgent
    if ([string]::IsNullOrWhiteSpace($runtimeVersion)) { throw 'Runtime version was not returned.' }
    if (-not $InspectOnly) {
        if ([string]::IsNullOrWhiteSpace($IngestUrl)) { $IngestUrl = 'http://127.0.0.1:5248/ingest/codex/hooks' }
        $endpoint = [Uri]($IngestUrl -replace '/hooks$', '/command-results')
        if ($endpoint.Scheme -notin @('http', 'https') -or -not $endpoint.AbsolutePath.EndsWith('/ingest/codex/command-results')) { throw 'Invalid capture endpoint.' }
        $client = [Net.Http.HttpClient]::new()
        $client.Timeout = [TimeSpan]::FromMilliseconds(500)
    }
    $count = 0
    foreach ($item in $turn[0].items) {
        if ($item.type -ne 'commandExecution' -or $item.status -notin @('completed', 'failed', 'declined')) { continue }
        $projection = [ordered]@{
            source = 'codex-app-server-command/v1'
            runtime_version = $runtimeVersion
            session_id = $SessionId.ToString()
            turn_id = $TurnId.ToString()
            item = [ordered]@{ type = $item.type; id = $item.id; status = $item.status; exitCode = $item.exitCode; commandActions = $item.commandActions }
        }
        $json = $projection | ConvertTo-Json -Depth 20 -Compress
        if ($InspectOnly) { $json; continue }
        $bytes = [Text.Encoding]::UTF8.GetBytes($json)
        if ($bytes.Length -gt 1048576) { throw 'Command metadata exceeds capture limit.' }
        $request = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Post, $endpoint)
        $request.Content = [Net.Http.ByteArrayContent]::new($bytes)
        $request.Content.Headers.ContentType = [Net.Http.Headers.MediaTypeHeaderValue]::new('application/json')
        $request.Headers.Add('X-Aigenterra-Observed-At', [DateTimeOffset]::UtcNow.ToString('O'))
        try {
            $response = $client.SendAsync($request, $deadline.Token).GetAwaiter().GetResult()
            try { if (-not $response.IsSuccessStatusCode) { throw 'Ingest rejected command metadata.' } }
            finally { $response.Dispose() }
        } finally { $request.Dispose() }
        $count++
    }
    if (-not $InspectOnly) { Write-Output "Captured $count terminal command records. Repeated capture is safe." }
} catch {
    # Fail-open for the observed runtime; explicit invocation still reports failure to the operator.
    Write-Error 'Command capture did not complete. Check the session/turn IDs, local Codex runtime and ingest endpoint; no command result was inferred.' -ErrorAction Continue
    exit 1
} finally {
    if ($null -ne $client) { $client.Dispose() }
    if ($null -ne $captureProcess) {
        if (-not $captureProcess.HasExited) { $captureProcess.Kill($true) }
        $captureProcess.Dispose()
    }
    if ($null -ne $deadline) { $deadline.Dispose() }
}
