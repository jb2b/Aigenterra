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

## Activity correlation (11)

`codex.evidence/1` appends versioned activity evidence separately from lifecycle
facts. Supported input is PreToolUse/PostToolUse hooks and explicitly contextualized
history MCP, commandExecution and fileChange items. Field locators reference the
original capture, request and context; output text is never reclassified as model
input. Unknown events/versions produce an auditable unsupported batch with no facts.

Activity identity is namespace + thread + turn + exact call/item ID. Child hook
agent_id owns its activity; session_id is not substituted. Missing turn/call IDs
produce unresolved, capture-specific activities. Names and timestamps never merge
calls. Repeated history adds provenance to one activity; four independent captures
may support one call. Evidence references include capture, normalizer version and
ordinal. Normalizer versions remain immutable and coexist.

PreToolUse means Requested, not proven execution. PostToolUse without explicit
isError remains ResultObserved. Explicit history terminal status outranks hook
terminal evidence; equal-authority disagreement remains TerminalUnknown/conflict.
Missing PostToolUse, absent errors, idle and error:null never imply success.
Coverage remains Partial. Upgrade requeues archived v2 inputs for the new evidence
interpreter, preserving old facts and original bytes; v1/legacy aggregates are not
modified. No new public inspection API is introduced.

Verification 11: solution restore/build/format-check, 213 tests and the isolated
hook probe passed. Includes sanitized research MCP history, explicit command/file
terminal conflicts, replay ordering/duplicates, namespace isolation and immutable
PostgreSQL evidence batches.

## Identity and segments (12)

`codex.evidence/2` composes the frozen activity interpreter with explicit agent,
turn-segment and relation evidence. Version 1 stays available. Agent keys contain
namespace/thread; segment keys add turn. A completed segment never marks the
agent permanently complete, so later turns reuse the same identity. Agent lifetime
remains Unknown. Contradictory metadata remains unresolved with conflict visible.

History parentThreadId and subAgentActivity.agentThreadId support explicit
parent/child relations. Delegation evidence keeps the parent turn and call ID,
but childTurnId and ParentTaskTurnId stay unknown without a proven explicit
mapping. Hook session_id is conversation context, not proof of direct parentage.
Empty collab receiver arrays, source/originator strings and rootTurnId alone do
not establish frontend ownership or task assignment. No legacy execution is
reopened or changed. Upgrade requeues immutable raw evidence for version 2.

Verification 12: solution restore/build/format-check, 216 tests and hook probe
passed. Research child reuse, three-child/four-segment identity, empty receivers,
unknown task assignment, hook lifetime limits and coexisting PostgreSQL versions
are covered; legacy execution invariants remain unchanged.

## Scoped scheduling, recovery and source gaps (13)

Use `codex reconcile --enrollment <absolute-json-path> --watch` with an explicitly
prepared local enrollment. Example (paths/IDs must be chosen by the operator):

```json
{
  "version": 1,
  "runtimeNamespaceId": "11111111-1111-4111-8111-111111111111",
  "sourceId": "22222222-2222-4222-8222-222222222222",
  "executable": "C:/tools/codex.exe",
  "codexHome": "C:/enrolled/codex-home",
  "apiBaseUrl": "http://127.0.0.1:5248",
  "stateDirectory": "C:/enrolled/aigenterra-reader-state",
  "hookSourceIds": ["33333333-3333-4333-8333-333333333333"],
  "threadIds": ["explicitly-selected-thread"]
}
```

The API must have `Runtime:NormalizeCaptures=true`. The watcher spools its
enrollment and diagnostics through the existing v2 archive, drains the spool,
and obtains eligible targets from
`GET /runtime/reconciliation/targets?runtimeNamespaceId=<uuid>&sourceId=<uuid>`.
Pages use optional `after` (thread ID) and `limit` (1–500); this discovery feed is
not a consistent inspection snapshot. Enrollment fingerprint must match locally.
Only explicitly selected roots, owners in the enrolled hook sources, and children
connected by explicit identity evidence are eligible. No thread/list or home-wide
chat scan occurs. Hook source IDs belong to this enrolled home/namespace; the
operator must not reuse them across homes. Reader identity is not owner identity.

Scheduling coalesces new semantic evidence for one second, refreshes target
discovery every five seconds, and aims at five seconds for active/unresolved
threads or sixty seconds for idle threads. Two independent readers maximum;
oldest due first. These are scheduling intervals, not delivery-latency guarantees.
Cursor/checkpoint writes are durable and atomically replaced. An interrupted
cycle is retried through a new reader; it never resumes Codex work. Changed local
enrollment requires a new state directory, preventing accidental home/ID mixing.
Watch owns spool delivery; do not run a competing drain worker on that spool.

Pending >=10,000 or any retained evidence pauses history reads and records spool
pressure. Existing hook capture remains independent and fail-open. A missing API,
target discovery failure, reader restart, read failure or exhausted page/time
budget becomes immutable recorder evidence. Successful reconciliation cannot
erase earlier gaps or reconstruct missing deltas. Original diagnostic inputs are
replayable; source age never implies idle failure.

`GET /runtime/sources/{sourceId}/reconciliation?runtimeNamespaceId=<uuid>` exposes
reader state, latest evidence, total gap count and the latest 100 gaps, with
no-store caching. OwnerState remains Unknown, Coverage Partial and Gate
KEEP_GATE_BLOCKED. Existing inventory health remains a separate contract.
Interrupted turns leave unfinished activities TerminalUnknown; late explicit
completed/failed activity evidence does not change the interrupted turn outcome.

Verification 13: solution restore/build/format-check, 220 tests and hook probe
passed. `research/codex/Test-ReconciliationRecovery.ps1` additionally exercised
the real installed CLI reader in the isolated copied-history home, API outage,
restart and persistent gaps against a disposable PostgreSQL database. Original
logs/checkpoints/spool remain local; the temporary database was removed. No
production sessions, configuration, trust or database were changed.
