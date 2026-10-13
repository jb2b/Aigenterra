#requires -Version 7.0
param([Parameter(Mandatory)][string] $Workspace)
$ErrorActionPreference = 'Stop'
$Workspace = (Resolve-Path -LiteralPath $Workspace).Path
$marker = Get-Content -LiteralPath (Join-Path $Workspace 'manifest.json') -Raw | ConvertFrom-Json
if ($marker.outputRoot -ne $Workspace) { throw 'Not a marked isolated hook-probe workspace.' }
$script = Join-Path $Workspace 'Synthetic-Mcp.ps1'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Synthetic-Mcp.ps1') -Destination $script
$config = Join-Path $Workspace 'home\config.toml'
if ((Get-Content -LiteralPath $config -Raw).Contains('[mcp_servers.observability_probe]')) { throw 'Probe MCP server already configured.' }
@"

[mcp_servers.observability_probe]
command = 'pwsh.exe'
args = ['-NoProfile', '-File', '$script', '-EvidenceDirectory', '$(Join-Path $Workspace 'evidence\mcp')']
startup_timeout_sec = 20
tool_timeout_sec = 30
"@ | Add-Content -LiteralPath $config -Encoding utf8NoBOM
Write-Output 'Configured only the isolated workspace MCP server.'
