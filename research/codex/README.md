# Isolated Codex observability research

These scripts and the later console spike are research tools, not Collector/API
features. They do not install hooks into the user's configuration. Generated
schemas, original bytes, stderr and copied runtime state stay outside Git.

## Hook probes

Use PowerShell 7, Git, the repository .NET SDK, an exact installed `codex.exe`,
and an already authenticated local account. Specify the executable and model:

```powershell
pwsh -NoProfile -File research/codex/Test-HookCapture.ps1
pwsh -NoProfile -File research/codex/Invoke-HookProbe.ps1 `
  -CodexPath C:\runtime\codex.exe -Model gpt-6-astra
```

Each invocation creates a new temporary home/repository/evidence directory.
Only `auth.json` is copied from the existing home, for the duration of the run,
then removed in `finally`. No private prompts, project config or source are
copied. Protect the local raw archive: runtime stderr/history can include paths
and model/tool content. Do not upload it or commit it wholesale.

The probe uses hooks/list to obtain the exact current hook hashes and writes
trust only into the temporary home. It verifies trust with another process,
then runs a bounded CLI turn. `-LeaveUntrusted` exercises disabled-by-trust
behavior; `-CodeMode` selects that feature in the temporary config. `-Prompt`
supports additional synthetic cases. Do not ask it to operate on real work.

The default shell/patch actions are permitted only if the runtime's effective
policy allows them. A denial is an experimental result; do not bypass policy.
`exitCode: 0` means the CLI turn finished, not that its tools succeeded.
The hook recorder writes exact stdin bytes and emits no stdout/stderr, including
on storage failure. `captureDurationMs` excludes PowerShell startup overhead.

```powershell
pwsh -NoProfile -File research/codex/Export-HookFixtures.ps1 `
  -ProbeRoot C:\path\to\temporary-probe `
  -Destination research/codex/fixtures/0.xxx/hooks-case
```

The exporter pseudonymizes UUIDs consistently within an invocation and replaces
probe/user paths. Review all strings before committing: this is deliberately
not a general secret-redaction policy. Manifests retain original-byte SHA-256,
length, runtime/model, trust and outcome. Sanitized JSON is not original evidence.
Do not use IDs from separately sanitized fixture sets to infer shared identity.

## Repository verification

`Invoke-Checks.ps1` runs solution restore, build, format verification, all tests
and the capture self-test. Supply `AIGENTERRA_TEST_POSTGRES_CONNECTION` for an
explicit disposable test server, or use the suite's normal Docker dependency.
Never point this research at a production database. Existing fixtures create
and delete only their own random test databases.

## Evidence status

See `docs/research/codex-modernization/` for measured results and limitations.
Running the Desktop-bundled executable in CLI mode is a binary comparison,
not proof that the Desktop client shares a passive subscription endpoint.

## Isolated .NET App Server spike

`CodexResearch.slnx` is intentionally separate from `Aigenterra.slnx`. No
production project references the spike. Its typed transport/session code is
small and uses opaque JSON at the experimental boundary, not generated DTOs.

```powershell
dotnet test research/codex/CodexResearch.slnx
dotnet format research/codex/CodexResearch.slnx --verify-no-changes
dotnet run --project research/codex/AppServerProbe -- `
  C:\runtime\codex.exe C:\path\to\isolated-hook-probe gpt-6-astra
```

The workspace must have the marker produced by Invoke-HookProbe.ps1. Each run
records exact received NDJSON bytes, sent requests and stderr separately. The
receipt-times sidecar contains metadata only. The spike creates a NEW test
thread, starts a harmless turn, waits for its explicit terminal notification
and reads its persisted state. `observed` is a capture result; inspect turnStatus
for the runtime outcome. Timeouts/errors are recorded as incomplete. Stdin EOF
is attempted before terminating only the process tree owned by the probe.

Use Export-AppServerFixtures.ps1 with -Workspace, -Evidence and -Destination
to export an allowlisted, jointly pseudonymized selection of BOTH sources.
Account notifications and reasoning items are excluded. Exact original bytes
remain local. Review strings; the exporter is not a universal secret scrubber.

For controlled MCP experiments, run Add-ProbeMcp.ps1 -Workspace on a marked
temporary workspace. It installs only the local Synthetic-Mcp.ps1 there. The
echo/fail tools are read-only; delay is bounded to 20 seconds and padding to
128 KiB. Test-SyntheticMcp.ps1 verifies its exact external response evidence.
Pass a synthetic prompt and scenario name as the optional fourth/fifth spike
arguments. The committed correlation report records the prompts' outcomes.
