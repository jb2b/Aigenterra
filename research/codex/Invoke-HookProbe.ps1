#requires -Version 7.0
param(
    [Parameter(Mandatory)][string] $CodexPath,
    [Parameter(Mandatory)][string] $Model,
    [string] $OutputRoot = (Join-Path ([IO.Path]::GetTempPath()) ('aigenterra-hooks-probe-' + [Guid]::NewGuid().ToString('N'))),
    [string] $AuthSource = (Join-Path $env:USERPROFILE '.codex\auth.json'),
    [string] $Prompt = "Use exactly one shell tool to execute Write-Output 'AIGENTERRA_PROBE_SHELL'. Then use apply_patch to create probe.txt containing AIGENTERRA_PROBE_PATCH. This is a disposable repository. Do not inspect other files, use network tools or spawn agents. Finish with PROBE_DONE.",
    [switch] $LeaveUntrusted,
    [switch] $CodeMode,
    [int] $TimeoutSeconds = 180
)
$ErrorActionPreference = 'Stop'
$CodexPath = (Resolve-Path -LiteralPath $CodexPath).Path
if ([IO.Path]::GetExtension($CodexPath) -ne '.exe') { throw 'Pass the exact codex.exe, not a shell wrapper.' }
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
if (Test-Path -LiteralPath $OutputRoot) { throw 'OutputRoot must be a new directory.' }
$probeHome = Join-Path $OutputRoot 'home'
$repo = Join-Path $OutputRoot 'repo'
$evidence = Join-Path $OutputRoot 'evidence'
$hookDirectory = Join-Path $evidence 'hooks'
foreach ($directory in @($probeHome, $repo, $evidence, $hookDirectory)) { [IO.Directory]::CreateDirectory($directory) | Out-Null }
& git -C $repo init --quiet
if ($LASTEXITCODE -ne 0) { throw 'Cannot initialize disposable repository.' }
$hookScript = Join-Path $OutputRoot 'Capture-Hook.ps1'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Capture-Hook.ps1') -Destination $hookScript
$events = @('SessionStart', 'SessionEnd', 'UserPromptSubmit', 'PreToolUse', 'PostToolUse', 'PermissionRequest', 'PreCompact', 'PostCompact', 'SubagentStart', 'SubagentStop', 'Stop', 'Interrupt')
$hooks = [ordered]@{}
foreach ($eventName in $events) {
    $hooks[$eventName] = @(@{ hooks = @(@{ type = 'command'; command = 'true'; commandWindows = "pwsh.exe -NoProfile -File `"$hookScript`" -Directory `"$hookDirectory`""; timeout = 2; async = $false }) })
}
@{ hooks = $hooks } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $probeHome 'hooks.json') -Encoding utf8NoBOM
$configPath = Join-Path $probeHome 'config.toml'
@"
model = '$Model'
model_reasoning_effort = 'low'
approval_policy = 'never'
cli_auth_credentials_store = 'file'
[analytics]
enabled = false
[features]
hooks = true
multi_agent = true
code_mode = $($CodeMode.IsPresent.ToString().ToLowerInvariant())
[projects.'$repo']
trust_level = 'trusted'
"@ | Set-Content -LiteralPath $configPath -Encoding utf8NoBOM

function New-ProbeStart([string[]] $Arguments) {
    $info = [Diagnostics.ProcessStartInfo]::new($CodexPath)
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.WorkingDirectory = $repo
    $info.RedirectStandardInput = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.Environment['CODEX_HOME'] = $probeHome
    foreach ($argument in $Arguments) { $info.ArgumentList.Add($argument) }
    return $info
}

function Read-HookInventory([string] $Name) {
    $process = [Diagnostics.Process]::Start((New-ProbeStart @('app-server')))
    $stderr = $process.StandardError.ReadToEndAsync()
    $received = [Collections.Generic.List[string]]::new()
    try {
        $initialize = @{ id = 1; method = 'initialize'; params = @{ clientInfo = @{ name = 'aigenterra_hook_probe'; version = '1' }; capabilities = @{ experimentalApi = $true } } }
        $process.StandardInput.WriteLine(($initialize | ConvertTo-Json -Depth 8 -Compress))
        $process.StandardInput.Flush()
        foreach ($responseId in @(1, 2)) {
            if ($responseId -eq 2) {
                $process.StandardInput.WriteLine('{"method":"initialized"}')
                $process.StandardInput.WriteLine((@{ id = 2; method = 'hooks/list'; params = @{ cwds = @($repo) } } | ConvertTo-Json -Depth 8 -Compress))
                $process.StandardInput.Flush()
            }
            do {
                $line = $process.StandardOutput.ReadLineAsync().WaitAsync([TimeSpan]::FromSeconds(30)).GetAwaiter().GetResult()
                if ($null -eq $line) { throw 'Inventory process closed stdout.' }
                $received.Add($line)
                $message = $line | ConvertFrom-Json -Depth 100
            } while ($message.id -ne $responseId)
            if ($null -ne $message.error) { throw ('Inventory RPC failed: ' + $message.error.code) }
        }
        return $message.result
    } finally {
        [IO.File]::WriteAllLines((Join-Path $evidence "$Name.ndjson"), $received)
        $process.StandardInput.Close()
        if (-not $process.WaitForExit(3000)) { $process.Kill($true); $process.WaitForExit() }
        [IO.File]::WriteAllText((Join-Path $evidence "$Name.stderr.txt"), $stderr.GetAwaiter().GetResult())
        $process.Dispose()
    }
}

try {
    if (Test-Path -LiteralPath $AuthSource) { Copy-Item -LiteralPath $AuthSource -Destination (Join-Path $probeHome 'auth.json') }
    $initial = Read-HookInventory 'hooks-untrusted'
    if (-not $LeaveUntrusted) {
        foreach ($hook in $initial.data.hooks) {
            if ($hook.sourcePath -ne (Join-Path $probeHome 'hooks.json')) { throw 'Unexpected hook source in isolated configuration.' }
            @"

[hooks.state.'$($hook.key)']
trusted_hash = '$($hook.currentHash)'
"@ | Add-Content -LiteralPath $configPath -Encoding utf8NoBOM
        }
    }
    $verified = Read-HookInventory 'hooks-effective'
    $process = [Diagnostics.Process]::Start((New-ProbeStart @('exec', '--json', '--color', 'never', '-C', $repo, '-s', 'workspace-write', $Prompt)))
    $stdoutFile = [IO.File]::Create((Join-Path $evidence 'cli.stdout.ndjson'))
    $stderrFile = [IO.File]::Create((Join-Path $evidence 'cli.stderr.txt'))
    $stdout = $process.StandardOutput.BaseStream.CopyToAsync($stdoutFile)
    $stderr = $process.StandardError.BaseStream.CopyToAsync($stderrFile)
    $process.StandardInput.Close()
    $timer = [Diagnostics.Stopwatch]::StartNew()
    $timedOut = -not $process.WaitForExit($TimeoutSeconds * 1000)
    if ($timedOut) { $process.Kill($true); $process.WaitForExit() }
    $stdout.GetAwaiter().GetResult() | Out-Null; $stderr.GetAwaiter().GetResult() | Out-Null
    $stdoutFile.Dispose(); $stderrFile.Dispose()
    $summary = [ordered]@{
        binaryVersion = (& $CodexPath --version | Out-String).Trim()
        model = $Model; codeMode = $CodeMode.IsPresent; leaveUntrusted = $LeaveUntrusted.IsPresent
        exitCode = $process.ExitCode; timedOut = $timedOut; elapsedMs = $timer.Elapsed.TotalMilliseconds
        inventory = @($verified.data.hooks | Select-Object eventName, enabled, trustStatus)
        hookCount = @(Get-ChildItem -LiteralPath $hookDirectory -Filter '*.bin').Count
        outputRoot = $OutputRoot
    }
    $process.Dispose()
    $summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $OutputRoot 'manifest.json') -Encoding utf8NoBOM
    $summary | ConvertTo-Json -Depth 8 -Compress
} finally {
    # Do not retain a second credential file with the research evidence.
    $copiedAuth = Join-Path $probeHome 'auth.json'
    if (Test-Path -LiteralPath $copiedAuth) { Remove-Item -LiteralPath $copiedAuth }
}
