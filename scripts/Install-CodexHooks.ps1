#requires -Version 7.0
param([Parameter(Mandatory)][string] $RepositoryPath, [switch] $SkipPublish)
$ErrorActionPreference = 'Stop'
$sourceRoot = Split-Path $PSScriptRoot -Parent
$repositoryRoot = (& git -C $RepositoryPath rev-parse --show-toplevel 2>$null)
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($repositoryRoot)) { throw 'Initialize the target Git repository first.' }
$repositoryRoot = $repositoryRoot.Trim()
$installPath = Join-Path $env:LOCALAPPDATA 'Aigenterra\collector'
if (-not $SkipPublish) {
    & dotnet publish (Join-Path $sourceRoot 'src\Aigenterra.Collector') -c Release -o $installPath
    if ($LASTEXITCODE -ne 0) { throw 'Collector publish failed; hooks were not changed.' }
}
if (-not (Test-Path -LiteralPath (Join-Path $installPath 'Aigenterra.Collector.exe'))) { throw 'Published Collector is missing.' }
$configurationPath = Join-Path $repositoryRoot '.codex\hooks.json'
$configuration = if (Test-Path -LiteralPath $configurationPath) {
    Get-Content -LiteralPath $configurationPath -Raw | ConvertFrom-Json -AsHashtable
} else { @{ hooks = @{} } }
if (-not $configuration.ContainsKey('hooks')) { $configuration.hooks = @{} }
# Use -File so an outer PowerShell cannot expand variables in a nested -Command.
# The launcher is installed beside the Collector, independently of the recorded checkout.
$launcherPath = Join-Path $installPath 'invoke-aigenterra-collector.ps1'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'invoke-aigenterra-collector.ps1') -Destination $launcherPath -Force
$command = 'pwsh.exe -NoProfile -File "' + $launcherPath + '"'
foreach ($eventName in @('SessionStart', 'UserPromptSubmit', 'PreToolUse', 'PostToolUse', 'SubagentStart', 'SubagentStop', 'Stop', 'Interrupt')) {
    $groups = @()
    foreach ($group in @($configuration.hooks[$eventName])) {
        if ($null -eq $group) { continue }
        $remaining = @($group.hooks | Where-Object {
            $_.commandWindows -notmatch 'invoke-aigenterra-collector\.ps1|Aigenterra\\collector\\Aigenterra\.Collector\.exe'
        })
        if ($remaining.Count -gt 0) { $group.hooks = $remaining; $groups += $group }
    }
    $groups += @{ hooks = @(@{ type = 'command'; command = 'true'; commandWindows = $command; timeout = 2; async = $false }) }
    $configuration.hooks[$eventName] = $groups
}
New-Item -ItemType Directory -Path (Split-Path $configurationPath -Parent) -Force | Out-Null
$temporaryPath = $configurationPath + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
$configuration | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $temporaryPath -Encoding utf8NoBOM
Move-Item -LiteralPath $temporaryPath -Destination $configurationPath -Force
Write-Output "Aigenterra hooks installed in $repositoryRoot. Commit .codex/hooks.json before creating new worktrees."

