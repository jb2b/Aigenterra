#requires -Version 7.0
param([string] $TestConnection = $env:AIGENTERRA_TEST_POSTGRES_CONNECTION)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
Push-Location $root
try {
    if ($TestConnection) { $env:AIGENTERRA_TEST_POSTGRES_CONNECTION = $TestConnection }
    & dotnet restore Aigenterra.slnx --nologo --verbosity quiet
    if ($LASTEXITCODE) { throw 'Solution restore failed.' }
    & dotnet build Aigenterra.slnx --no-restore --nologo --verbosity quiet
    if ($LASTEXITCODE) { throw 'Solution build failed.' }
    & dotnet format Aigenterra.slnx --verify-no-changes --no-restore --verbosity quiet
    if ($LASTEXITCODE) { throw 'Solution format verification failed.' }
    # Existing recording smoke tests have a 500 ms Git-context budget. Keep this
    # research verification deterministic under concurrent database/process load.
    & dotnet test Aigenterra.slnx --no-build --no-restore --nologo --verbosity minimal -m:1
    if ($LASTEXITCODE) { throw 'Solution tests failed.' }
    & (Join-Path $PSScriptRoot 'Test-HookCapture.ps1')
} finally { Pop-Location }
