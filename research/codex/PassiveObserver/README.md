# Research-only passive reader

Used by [research commit 6.5](../../../docs/research/codex-modernization/passive-revalidation-20261013.md).
The observed work must be created and operated by a normal CLI or actual Desktop
UI. This program has no thread-start/resume/control path. It captures server
requests without answering them. It is not a production observer or a promise
that connection establishment has no server-side effects.

```powershell
dotnet restore research/codex/CodexResearch.slnx
dotnet build research/codex/CodexResearch.slnx --no-restore
dotnet run --no-build --project research/codex/PassiveObserver -- `
  <exact-codex.exe> <existing-test-CODEX_HOME> <test-cwd> <known-test-thread-id> `
  <new-private-evidence-directory> unix 600
```

`unix` connects a WebSocket directly to the existing home control socket; it never
starts or manages a daemon. `history` starts a separate stdio reader against that
home and is explicitly not a same-runtime live attachment. `proxy` preserves the
initial negative framing experiment: newline JSON-RPC through a raw WebSocket
proxy is not a supported client framing mode and must not be used as an absence
of capability test. Prefer `unix` for the advertised Unix endpoint.

The only requests are initialize and loaded/read/turn-history snapshots at the
beginning and end, plus initialized notification. A maximum of one 100-turn page
is requested per snapshot: non-null cursor means incomplete history. The idle
interval receives notifications and performs no polling. Create an empty `stop`
file in the output directory to end it early. This closes only the observer's
socket/child reader; it never sends thread/unsubscribe or controls the owner.
Inspect `summary.json.failure`; a missing graceful close handshake is not success.

For stdio, `received.ndjson` preserves received bytes before parsing. For Unix
WebSocket, `payload-*.bin` preserves decoded application payload chunks before
parsing (including partial failure), and `received.ndjson` is derived framing.
Incoming messages have a 64 MiB memory limit. These bounded research paths do not
provide production spooling, retention, replay, durability or backpressure claims.
The output can contain private data and must stay outside tracked paths.

Owner setup uses a fresh, short disposable home/repository, preconfigured synthetic
MCP and temporary authentication; the normal CLI auto-starts its daemon. Do not
point a managed startup/cleanup command at an existing user home. Use a new
projectless chat in the real Desktop UI for that surface and do not edit its
existing configuration. Obtain only the test thread ID. Do not use this program
to create the observed workload or use a bundled executable as a substitute UI.

The experiment archive has `cli-evidence` and `actual-desktop` connection folders,
version audits and schema hashes. Export to **new** versioned directories with
`Export-PassiveFixtures.ps1 -Archive <private-archive> -Surface cli|desktop
-Destination <new-fixture-directory>`. The exporter refuses an existing destination.
Review the selected strings; it is an allowlist for these synthetic probes, not a
general-purpose secret scrubber. Test with:

```powershell
dotnet test research/codex/CodexResearch.slnx --no-build --no-restore
dotnet format research/codex/CodexResearch.slnx --verify-no-changes --no-restore
```
