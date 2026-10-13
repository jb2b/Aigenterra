#requires -Version 7.0
param([Parameter(Mandatory)][string] $EvidenceDirectory)
$ErrorActionPreference = 'Stop'
[IO.Directory]::CreateDirectory($EvidenceDirectory) | Out-Null
$instance = [Guid]::NewGuid().ToString('N')
$responsePath = Join-Path $EvidenceDirectory "$instance.responses.ndjson"
$requestPath = Join-Path $EvidenceDirectory "$instance.requests.ndjson"
$stdout = [Console]::OpenStandardOutput()
while ($null -ne ($line = [Console]::In.ReadLine())) {
    [IO.File]::AppendAllText($requestPath, $line + "`n", [Text.UTF8Encoding]::new($false))
    $request = $line | ConvertFrom-Json -Depth 100
    if ($null -eq $request.id) { continue }
    $reply = @{ jsonrpc = '2.0'; id = $request.id }
    switch ($request.method) {
        'initialize' { $reply.result = @{ protocolVersion = $request.params.protocolVersion; capabilities = @{ tools = @{} }; serverInfo = @{ name = 'aigenterra-synthetic-probe'; version = '1' } } }
        'ping' { $reply.result = @{} }
        'tools/list' {
            $schema = @{ type = 'object'; properties = @{ marker = @{ type = 'string' }; delayMs = @{ type = 'integer'; minimum = 0; maximum = 20000 }; size = @{ type = 'integer'; minimum = 0; maximum = 131072 } }; required = @('marker'); additionalProperties = $false }
            $reply.result = @{ tools = @(
                    @{ name = 'echo'; description = 'Read-only synthetic observability result. Echo marker, optionally delay and produce bounded padding.'; inputSchema = $schema; annotations = @{ readOnlyHint = $true; destructiveHint = $false; openWorldHint = $false; idempotentHint = $true } },
                    @{ name = 'fail'; description = 'Read-only synthetic MCP error result for observability testing.'; inputSchema = $schema; annotations = @{ readOnlyHint = $true; destructiveHint = $false; openWorldHint = $false; idempotentHint = $true } }
                ) }
        }
        'tools/call' {
            $inputArgs = $request.params.arguments
            $delay = [Math]::Clamp([int]$inputArgs.delayMs, 0, 20000)
            $size = [Math]::Clamp([int]$inputArgs.size, 0, 131072)
            if ($delay) { Start-Sleep -Milliseconds $delay }
            $failed = $request.params.name -eq 'fail'
            $value = @{ marker = [string]$inputArgs.marker; padding = ('x' * $size); result = $(if ($failed) { 'PROBE_ERROR' } else { 'PROBE_OK' }) }
            $reply.result = @{ content = @(@{ type = 'text'; text = ($value | ConvertTo-Json -Compress) }); structuredContent = $value; _meta = @{ origin = 'synthetic-server'; evidenceVersion = 1 }; isError = $failed }
        }
        default { $reply.error = @{ code = -32601; message = 'Method not implemented by the research server.' } }
    }
    # Record the EXACT bytes subsequently emitted, including the LF delimiter.
    $bytes = [Text.Encoding]::UTF8.GetBytes(($reply | ConvertTo-Json -Depth 100 -Compress) + "`n")
    $file = [IO.File]::Open($responsePath, [IO.FileMode]::Append, [IO.FileAccess]::Write, [IO.FileShare]::Read)
    try { $file.Write($bytes); $file.Flush($true) } finally { $file.Dispose() }
    $stdout.Write($bytes); $stdout.Flush()
}
