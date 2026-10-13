# Approved runtime reconciliation: commits 10–14

This amendment records the user's approval of the revised production design on
2026-10-13. Historical research, including commit 6.5, remains unchanged.
**KEEP_GATE_BLOCKED** remains the verdict for complete live runtime observation.
Hooks supply event-specific live evidence where available. App Server supplies
passive history/state and explicit identity evidence. Neither implies complete
coverage. No passive-live transport workaround is authorized.

Commit 10 is split into 10a (durable processing and verified request context) and
10b (isolated history reader). This isolates archive/queue failure semantics from
subprocess/protocol failure semantics, making each commit independently testable.
Commits 11–14 add activities, agent identity/segments, scoped reconciliation/gaps,
and deterministic projection generations. Stop after 14; no snapshot API or UI.

## Durable acceptance (10a)

The unchanged v2 envelope is archived with a durable work row in the same
transaction. Receipts now consistently say `normalization: pending`; downstream
failures cannot revoke archive acceptance. Existing normalization and health
flags enable background processing, independently; default remains archive-only.
Upgrade backfills one work item per archived v2 capture. Workers use leases and
per-capture completion, never a maximum allocated ingestion ID as a watermark.
Retries retain an outcome and reuse deterministic normalization attempt identity.

Requests and responses keep their original bytes. A separate v2 diagnostic with
provenance `request_context` contains version 1, requestCaptureId,
responseCaptureId and cycleId. The adapter validates namespace, source, epoch,
version, RPC IDs, allowed read method and explicit thread parameters before
storing an immutable link. Missing dependencies stay pending; malformed context
is rejected. Recorder context is never model-visible evidence. No envelope
serialization/fingerprint changes are made.

Rollback disables processing flags and new producers; retain all evidence and
tables. No production database is migrated. Existing v1 ingestion stays intact.

Verification 10a: solution restore/build/format-check, 198 tests and the isolated
hook byte-fidelity/fail-open probe passed. PostgreSQL tests ran only in disposable
databases on the research loopback cluster. No production deployment occurred.

## Passive reader (10b)

Explicit one-shot use (spool first; delivery remains the separate `drain` command):

```text
dotnet Aigenterra.Collector.dll codex reconcile --executable <absolute-codex.exe> --home <enrolled-absolute-home> --namespace <uuid> --source <uuid> --thread <explicit-thread-id> [--cursor <cursor>]
```

The reader owns a new `codex app-server` stdio process with the explicitly selected
home. Only initialize/initialized and passive thread reads are permitted. It reads
thread metadata then turns with full persisted items, 100 per page, ascending.
Maximum 100 pages/30 seconds per read cycle and 10 seconds per request. A budget
result exposes the continuation cursor; paging is not an atomic runtime snapshot.
Ctrl+C, timeout, EOF, malformed frames, repeated cursors and unexpected server
requests stop this reader. No approvals are answered. Shutdown closes its stdin
and, if necessary, terminates only its own Process object, never an owner/daemon.

Each raw stdout frame (including CR/LF), outgoing request and stderr chunk is
spooled before interpretation. Oversize evidence remains retained locally;
partial frames survive EOF/timeout. `reader_frame` notifications remain diagnostic
evidence, not owner-live events. The separately pinned `codex.history/1` normalizer
uses verified request context for paginated turns. `codex.lifecycle/1` is unchanged.
Unknown versions, unmatched context and unsupported fields never gain invented IDs.

Manual smoke: installed CLI 0.162.1 and Desktop runtime 0.160.1 both read copied
research histories successfully in separate temporary homes, without credentials,
model work or a daemon. Executable SHA-256 values match research 6.5. Original
wire/spool evidence remains local under `.codex/artifacts/aigenterra-production-10-14`.
These are isolated reader tests using prior actual-frontend research histories;
they do not retest active owner neutrality or lift KEEP_GATE_BLOCKED.

Verification 10b: solution restore/build/format-check, 208 tests, isolated hook
probe and both isolated real-runtime history reads passed. No new live capability
is claimed. Request context, pagination, partial/oversize frames, timeout, EOF,
malformed/unmatched responses and unexpected server requests are covered.
