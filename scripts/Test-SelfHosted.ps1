#requires -Version 7.0
[CmdletBinding()]
param([switch] $RealCodex)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$project = 'aigenterra-smoke-' + [Guid]::NewGuid().ToString('N')
$composeArgs = @('compose', '--project-directory', $repositoryRoot,
    '--env-file', (Join-Path $repositoryRoot '.env.example'),
    '-f', (Join-Path $repositoryRoot 'docker-compose.yml'), '-p', $project)
$previousEnvironment = @{}
$resourcesMayExist = $false
foreach ($key in @('POSTGRES_PASSWORD', 'WEB_PORT', 'WEB_BIND_HOST', 'AIGENTERRA_INGEST_URL', 'AIGENTERRA_SPOOL_PATH', 'AIGENTERRA_CAPTURE_URL')) {
    $previousEnvironment[$key] = [Environment]::GetEnvironmentVariable($key, 'Process')
}

function Invoke-Docker {
    param([Parameter(ValueFromRemainingArguments)] [string[]] $CommandArgs)
    & docker @CommandArgs
    if ($LASTEXITCODE -ne 0) { throw "Docker command failed (exit $LASTEXITCODE)." }
}

function Invoke-Compose {
    param([Parameter(ValueFromRemainingArguments)] [string[]] $CommandArgs)
    Invoke-Docker @composeArgs @CommandArgs
}

function Assert-That([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

function Get-Response([string] $Path) {
    Convert-Response (Invoke-WebRequest "$script:origin$Path" -SkipHttpErrorCheck -TimeoutSec 10)
}

function Convert-Response($Response) {
    # Some PowerShell versions expose application/problem+json as bytes rather than text.
    [pscustomobject]@{
        StatusCode = [int]$Response.StatusCode
        Headers = $Response.Headers
        Content = if ($Response.Content -is [byte[]]) {
            [Text.Encoding]::UTF8.GetString($Response.Content)
        } else { [string]$Response.Content }
    }
}

function Wait-Status([string] $Path, [int] $Expected) {
    $deadline = [DateTime]::UtcNow.AddSeconds(60)
    do {
        try {
            $response = Get-Response $Path
            if ([int]$response.StatusCode -eq $Expected) { return }
        }
        catch { } # A transient connection failure is expected while restarting.
        # Poll a concrete readiness condition, never delay startup by a fixed sleep.
        Start-Sleep -Milliseconds 500
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "Timed out waiting for $Path to return $Expected."
}

function Send-Hook([byte[]] $Payload, [string] $ObservedAt) {
    Convert-Response (Invoke-WebRequest "$script:origin/api/ingest/codex/hooks" -Method Post -Body $Payload `
        -ContentType 'application/json' -Headers @{ 'X-Aigenterra-Observed-At' = $ObservedAt } `
        -SkipHttpErrorCheck -TimeoutSec 10)
}

function Get-Evidence([string] $ObservationId) {
    # Only IDs returned by the API enter SQL, and validate them before interpolation.
    $id = [Guid]::Parse($ObservationId).ToString()
    $sql = "SELECT encode(evidence, 'hex') FROM observations WHERE id = '$id';"
    (Invoke-Compose exec -T postgres psql -U aigenterra -d aigenterra -Atc $sql) -join "`n"
}

function Send-Json([string] $Path, $Body) {
    Convert-Response (Invoke-WebRequest "$script:origin$Path" -Method Post `
        -Body ([Text.Encoding]::UTF8.GetBytes(($Body | ConvertTo-Json -Depth 30 -Compress))) `
        -ContentType 'application/json' -Headers @{ 'X-Aigenterra-Observed-At' = [DateTimeOffset]::UtcNow.ToString('O') } `
        -SkipHttpErrorCheck -TimeoutSec 30)
}

function Test-Evaluations([string] $TaskId, [string] $SessionId, [string] $TurnId) {
    $path = "/api/task-executions/$TaskId/evaluations"
    $first = Send-Json $path $null
    Assert-That ($first.StatusCode -eq 201) 'Evaluation creation failed.'
    $initial = $first.Content | ConvertFrom-Json
    Assert-That (($initial.findings | Where-Object detection -ne 'NotObserved').Count -eq 0) 'Empty evidence produced a conclusion.'
    Assert-That ((Send-Json $path $null).StatusCode -eq 200) 'Same snapshot was not reused.'
    $fixtureRoot = Join-Path $repositoryRoot 'tests/Aigenterra.Codex.Tests/Fixtures'
    $hooks = (Get-Content -Raw (Join-Path $fixtureRoot 'codex-0.153.4-command-hooks.json') | ConvertFrom-Json -AsHashtable).events
    foreach ($hook in $hooks) {
        $hook.Remove('keys'); $hook.session_id = $SessionId; $hook.turn_id = $TurnId
        Assert-That ((Send-Json '/api/ingest/codex/hooks' $hook).StatusCode -eq 200) 'Fixture hook rejected.'
    }
    $hookOnly = (Send-Json $path $null).Content | ConvertFrom-Json
    Assert-That (@($hookOnly.findings.attempts | Where-Object verdict -ne 'Unknown').Count -eq 0) 'Hook stdout was mistaken for exit evidence.'
    $captures = Get-Content -Raw (Join-Path $fixtureRoot 'codex-0.153.4-command-results.json') | ConvertFrom-Json -AsHashtable
    foreach ($capture in $captures) {
        $capture.session_id = $SessionId; $capture.turn_id = $TurnId
        $response = Send-Json '/api/ingest/codex/command-results' $capture
        Assert-That ($response.StatusCode -eq 200) 'Terminal command capture rejected.'
        $replay = (Send-Json '/api/ingest/codex/command-results' $capture).Content | ConvertFrom-Json
        Assert-That ($replay.disposition -eq 'duplicate') 'Capture replay allocated new evidence.'
    }
    $evaluatedResponse = Send-Json $path $null
    $evaluated = $evaluatedResponse.Content | ConvertFrom-Json
    Assert-That ($evaluatedResponse.StatusCode -eq 201 -and $evaluated.evaluationId -ne $initial.evaluationId) 'New evidence did not create a new evaluation.'
    foreach ($finding in $evaluated.findings) {
        Assert-That ($finding.attempts.Count -eq 2) 'Lost a command attempt.'
        Assert-That (@($finding.attempts | Where-Object verdict -eq 'Succeeded').Count -eq 1) 'Missing successful command.'
        Assert-That (@($finding.attempts | Where-Object verdict -eq 'Failed').Count -eq 1) 'Missing failed command.'
        foreach ($attempt in $finding.attempts) { Assert-That ($attempt.observationIds.Count -eq 3) 'Evidence chain incomplete.' }
    }
    $location = [Uri]::new([Uri]"$script:origin$path", [string]($evaluatedResponse.Headers.Location | Select-Object -First 1))
    Assert-That ($location.AbsolutePath -eq "$path/$($evaluated.evaluationId)") 'Location is invalid behind the web proxy.'
    Assert-That (-not ($evaluatedResponse.Content -match 'tool_response|commandActions|dotnet build|dotnet test')) 'Evaluation leaked raw evidence or commands.'
    Assert-That (((Get-Response "$path/$($initial.evaluationId)").Content | ConvertFrom-Json).hasNewerEvidence) 'Old snapshot is not marked stale.'
    Assert-That (((Get-Response $path).Content | ConvertFrom-Json).items.Count -eq 3) 'Evaluation history is incomplete.'
    Assert-That ((Get-Response ($path + '?cursor=invalid')).StatusCode -eq 400) 'Invalid cursor not rejected.'
    Assert-That ((Get-Response "/api/task-executions/$([Guid]::NewGuid())/evaluations/$($evaluated.evaluationId)").StatusCode -eq 404) 'Cross-task evaluation exposed.'
    return "$path/$($evaluated.evaluationId)"
}

try {
    Assert-That ((Invoke-Docker info --format '{{.OSType}}') -eq 'linux') 'Docker must run Linux containers.'
    $env:POSTGRES_PASSWORD = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $listener.Start()
    $env:WEB_PORT = [string]$listener.LocalEndpoint.Port
    $listener.Stop()
    $env:WEB_BIND_HOST = '127.0.0.1'
    $script:origin = "http://127.0.0.1:$env:WEB_PORT"

    Write-Host "Testing isolated Linux stack $project at $script:origin"
    Invoke-Compose config --quiet
    # Inspect in memory: expanded config contains the test password and must not be printed.
    $configuration = (Invoke-Compose config --format json) -join "`n" | ConvertFrom-Json -AsHashtable
    Assert-That ($configuration.services.Count -eq 3) 'Expected exactly three services.'
    Assert-That (-not $configuration.services.api.ContainsKey('ports')) 'Api publishes a host port.'
    Assert-That (-not $configuration.services.postgres.ContainsKey('ports')) 'PostgreSQL publishes a host port.'
    Assert-That ($configuration.services.web.ports[0].host_ip -eq '127.0.0.1') 'Web must bind to loopback.'

    $resourcesMayExist = $true
    Invoke-Compose up -d --build --wait --wait-timeout 180 | Out-Host
    foreach ($service in @('postgres', 'api', 'web')) {
        $id = Invoke-Compose ps -q $service
        Assert-That ((Invoke-Docker inspect --format '{{.State.Health.Status}}' $id) -eq 'healthy') "$service is not healthy."
        $image = Invoke-Docker inspect --format '{{.Image}}' $id
        Assert-That ((Invoke-Docker image inspect --format '{{.Os}}' $image) -eq 'linux') "$service image is not Linux."
        $imageMetadata = ((Invoke-Docker image inspect --format '{{json .Config}}' $image) -join "`n") +
            ((Invoke-Docker history --no-trunc --format '{{.CreatedBy}}' $image) -join "`n")
        Assert-That (-not $imageMetadata.Contains($env:POSTGRES_PASSWORD)) "$service image contains the runtime password."
        if ($service -ne 'postgres') {
            Assert-That ((Invoke-Compose exec -T $service id -u) -ne '0') "$service runs as root."
        }
    }
    Assert-That ((Invoke-Compose exec -T api dotnet --list-sdks).Count -eq 0) 'API runtime contains a .NET SDK.'
    $apiFiles = @(Invoke-Compose exec -T api find /app -type f)
    $webFiles = @(Invoke-Compose exec -T web find /usr/share/nginx/html -type f)
    $unwanted = @($apiFiles + $webFiles | Where-Object {
        $_ -match '(^|/)(\.env|\.git|node_modules|secrets|evidence|CodexScope)|\.(cs|csproj|ps1|log)$|appsettings\.Development'
    })
    Assert-That ($unwanted.Count -eq 0) 'Final images contain source files, development settings or captured output.'
    $migrationCount = Invoke-Compose exec -T postgres psql -U aigenterra -d aigenterra -Atc 'SELECT count(*) FROM "__EFMigrationsHistory";'
    Assert-That ($migrationCount -eq '5') 'Expected all five reviewed migrations.'

    $page = Get-Response /executions
    Assert-That ($page.StatusCode -eq 200 -and $page.Content.Contains('<app-root')) 'SPA did not load.'
    Assert-That (($page.Headers['Cache-Control'] -join ',') -match 'no-cache') 'SPA must revalidate.'
    $asset = [regex]::Match($page.Content, 'src="([^"]+\.js)"').Groups[1].Value
    Assert-That (-not [string]::IsNullOrWhiteSpace($asset)) 'No script asset found.'
    $assetResponse = Get-Response "/$asset"
    Assert-That ($assetResponse.StatusCode -eq 200) 'Production script missing.'
    Assert-That (($assetResponse.Headers['Cache-Control'] -join ',') -match 'immutable') 'Hashed assets must be immutable.'
    $favicon = Get-Response /favicon.svg
    Assert-That (($favicon.Headers['Cache-Control'] -join ',') -match 'no-cache') 'Unhashed assets must revalidate.'
    Assert-That ((Get-Response /missing.js).StatusCode -eq 404) 'Missing asset returned the SPA.'
    Assert-That ((Get-Response /.env).StatusCode -eq 404) 'Dotfile must not be served.'
    $unknownApi = Get-Response /api/does-not-exist
    Assert-That ($unknownApi.StatusCode -eq 404 -and -not $unknownApi.Content.Contains('<app-root')) 'API 404 was replaced with SPA.'
    foreach ($limit in @(0, 101)) {
        $badLimit = Get-Response "/api/task-executions?limit=$limit"
        Assert-That ($badLimit.StatusCode -eq 400) 'Querystring/validation was lost by proxy.'
        Assert-That (($badLimit.Content | ConvertFrom-Json).code -eq 'invalid_execution_limit') 'Problem Details contract changed.'
    }
    Assert-That ((Get-Response "/api/task-executions/$([Guid]::NewGuid())/timeline").StatusCode -eq 404) 'Unknown execution must return 404.'

    $session = [Guid]::NewGuid().ToString()
    $turn = [Guid]::NewGuid().ToString()
    $observedAt = [DateTimeOffset]::UtcNow.ToString('O')
    # Whitespace, Unicode and CRLF exercise raw byte preservation across nginx and Api.
    $payload = [Text.Encoding]::UTF8.GetBytes("{ `"hook_event_name`":`"UserPromptSubmit`", `"session_id`":`"$session`", `"turn_id`":`"$turn`", `"prompt`":`"smoke héllo`" }`r`n")
    $response = Send-Hook $payload $observedAt
    Assert-That ($response.StatusCode -eq 200) 'Hook ingest failed.'
    $receipt = $response.Content | ConvertFrom-Json
    $duplicate = (Send-Hook $payload $observedAt).Content | ConvertFrom-Json
    Assert-That ($receipt.taskExecutionId -eq $duplicate.taskExecutionId -and $receipt.observationId -eq $duplicate.observationId) 'Hook redelivery was not idempotent.'
    $hex = [Convert]::ToHexString($payload).ToLowerInvariant()
    Assert-That ((Get-Evidence $receipt.observationId) -ceq $hex) 'Raw evidence bytes changed in transit.'
    $stop = [Text.Encoding]::UTF8.GetBytes((@{ hook_event_name = 'Stop'; session_id = $session; turn_id = $turn; stop_hook_active = $false; last_assistant_message = 'Done' } | ConvertTo-Json -Compress))
    Assert-That ((Send-Hook $stop ([DateTimeOffset]::UtcNow.ToString('O'))).StatusCode -eq 200) 'Stop hook failed.'
    $timelinePath = "/api/task-executions/$($receipt.taskExecutionId)/timeline"
    $before = (Get-Response $timelinePath).Content | ConvertFrom-Json -AsHashtable
    Assert-That ($before.status -eq 'completed' -and $before.observations.Count -eq 2 -and $before.agents[0].status -eq 'completed') 'Completed turn/agent or timeline incorrect.'
    Assert-That ($before.observations[0].hasRawEvidence) 'Evidence indicator missing.'
    $recent = (Get-Response '/api/task-executions?limit=1').Content | ConvertFrom-Json
    Assert-That ($recent.items[0].taskExecutionId -eq $receipt.taskExecutionId) 'Execution missing from overview.'
    $badHeader = Send-Hook $payload 'not-a-timestamp'
    Assert-That ($badHeader.StatusCode -eq 400 -and ($badHeader.Content | ConvertFrom-Json).code -eq 'observed_at_header_invalid') 'ObservedAt validation not preserved.'
    $template = '{"hook_event_name":"FutureHook","padding":""}'
    $large = [Text.Encoding]::UTF8.GetBytes($template.Insert($template.Length - 2, ('x' * (1048576 - $template.Length))))
    Assert-That ((Send-Hook $large $observedAt).StatusCode -eq 200) 'Valid 1 MiB payload rejected by proxy.'
    Assert-That ((Send-Hook ($large + [byte]32) $observedAt).StatusCode -eq 413) 'Oversized payload not rejected.'

    $evaluationPath = Test-Evaluations $receipt.taskExecutionId $session $turn
    $evaluationBefore = (Get-Response $evaluationPath).Content
    $before = (Get-Response $timelinePath).Content | ConvertFrom-Json -AsHashtable

    if ($RealCodex) {
        $env:AIGENTERRA_INGEST_URL = "$script:origin/api/ingest/codex/hooks"
        $env:AIGENTERRA_CAPTURE_URL = "$script:origin/api/ingest/codex/captures/v1"
        $env:AIGENTERRA_SPOOL_PATH = Join-Path ([IO.Path]::GetTempPath()) $project
        $probePrompt = 'Aigenterra real command-result acceptance. Do not edit files, use subagents, plugins or MCP. Run exactly four separate exec commands from this repository: dotnet build src/Aigenterra.Domain/Aigenterra.Domain.csproj --no-restore; dotnet build Aigenterra.Nonexistent.Probe.csproj --no-restore; dotnet test tests/Aigenterra.Infrastructure.Tests/Aigenterra.Infrastructure.Tests.csproj --no-restore; dotnet test Aigenterra.Nonexistent.Probe.csproj --no-restore. Semicolons in this instruction separate calls, not shell commands. Use yield_time_ms 1000 and poll returned sessions with write_stdin until completion. Nonexistent-project errors are expected. End with a short receipt; do not run anything else.'
        # JSON output is kept in memory, never printed or stored alongside user data.
        $probeOutput = & codex exec --json --sandbox read-only $probePrompt
        Assert-That ($LASTEXITCODE -eq 0) 'Real Codex acceptance turn failed.'
        & (Join-Path $env:LOCALAPPDATA 'Aigenterra\collector\Aigenterra.Collector.exe') drain
        Assert-That ($LASTEXITCODE -eq 0) 'Real hook spool did not drain.'
        $probeEvents = @($probeOutput | ForEach-Object { try { $_ | ConvertFrom-Json } catch {} })
        $realSession = @($probeEvents | Where-Object type -eq 'thread.started')[0].thread_id
        $execution = @(((Get-Response '/api/task-executions?limit=50').Content | ConvertFrom-Json).items | Where-Object objective -eq $probePrompt)[0]
        Assert-That ($null -ne $execution) 'Real hooks did not record the acceptance turn.'
        $realTurn = $execution.runtimeReference.externalId
        & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'Capture-CodexCommandResults.ps1') -SessionId $realSession -TurnId $realTurn
        Assert-That ($LASTEXITCODE -eq 0) 'Real runtime capture failed.'
        $realPath = "/api/task-executions/$($execution.taskExecutionId)/evaluations"
        $realResult = (Send-Json $realPath $null).Content | ConvertFrom-Json
        foreach ($finding in $realResult.findings) {
            Assert-That ($finding.attempts.Count -eq 2) 'Real command attempts are incomplete.'
            Assert-That (@($finding.attempts | Where-Object verdict -eq 'Succeeded').Count -eq 1) 'Real success not proven.'
            Assert-That (@($finding.attempts | Where-Object verdict -eq 'Failed').Count -eq 1) 'Real failure not proven.'
        }
        $realEvaluationPath = "$realPath/$($realResult.evaluationId)"
        $realEvaluationBefore = (Get-Response $realEvaluationPath).Content
        Write-Host "Real evaluation verified: $($execution.taskExecutionId)"
    }

    Invoke-Compose down | Out-Host
    Invoke-Compose up -d --wait --wait-timeout 180 | Out-Host
    $after = (Get-Response $timelinePath).Content | ConvertFrom-Json -AsHashtable
    Assert-That (($before | ConvertTo-Json -Depth 20 -Compress) -ceq ($after | ConvertTo-Json -Depth 20 -Compress)) 'Timeline changed after down/up.'
    Assert-That ((Get-Evidence $receipt.observationId) -ceq $hex) 'Evidence changed after down/up.'
    Assert-That ((Get-Response $evaluationPath).Content -ceq $evaluationBefore) 'Evaluation changed after down/up.'
    if ($RealCodex) { Assert-That ((Get-Response $realEvaluationPath).Content -ceq $realEvaluationBefore) 'Real evaluation changed after down/up.' }
    $detail = Get-Response "/executions/$($receipt.taskExecutionId)"
    Assert-That ($detail.StatusCode -eq 200 -and $detail.Content.Contains('<app-root')) 'Direct detail refresh failed.'

    Invoke-Compose stop postgres | Out-Host
    Wait-Status /api/health 503
    Assert-That ((Get-Response /api/alive).StatusCode -eq 200) 'Database outage broke liveness.'
    Invoke-Compose up -d --wait --wait-timeout 180 postgres | Out-Host
    Wait-Status /api/health 200
    $webId = Invoke-Compose ps -q web
    Invoke-Compose up -d --no-deps --force-recreate --wait --wait-timeout 180 api | Out-Host
    Wait-Status /api/health 200
    Assert-That ((Invoke-Compose ps -q web) -eq $webId) 'Api replacement required replacing Web.'
    Assert-That ((Get-Response $timelinePath).StatusCode -eq 200) 'Timeline unavailable after Api replacement.'

    $queryMarker = 'query-must-not-be-logged-' + [Guid]::NewGuid().ToString('N')
    Assert-That ((Get-Response "/api/task-executions?limit=1&marker=$queryMarker").StatusCode -eq 200) 'Query probe failed.'
    $logs = (Invoke-Compose logs --no-color) -join "`n"
    Assert-That (-not $logs.Contains($env:POSTGRES_PASSWORD)) 'Password was written to container logs.'
    Assert-That (-not $logs.Contains('smoke héllo')) 'Payload was written to container logs.'
    Assert-That (-not $logs.Contains($queryMarker)) 'Querystring was written to container logs.'
    Write-Host 'PASS: Linux topology, migrations, routing, ingest, evaluations, durability and outage recovery.'
}
finally {
    try {
        # Both the name prefix and the random suffix are owned by this invocation.
        if ($resourcesMayExist -and $project -match '^aigenterra-smoke-[0-9a-f]{32}$') {
            Invoke-Compose down --volumes --remove-orphans | Out-Host
            # Compose assigns images this unique project name; do not remove shared base images.
            foreach ($service in @('api', 'web')) {
                & docker image rm "$project-$service" 2>$null | Out-Null
            }
            Write-Host "Removed disposable test stack and volume: $project (test data cannot be recovered)."
        }
    }
    finally {
        foreach ($entry in $previousEnvironment.GetEnumerator()) {
            [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value, 'Process')
        }
    }
}
