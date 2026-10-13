# Runtime foundation: authorized commits 7–9

The user approved commits 7–9 independently of the live attachment gate on
2026-10-13. Research reports and fixtures remain historical evidence. Commit 10
(connection/observation transport) remains blocked and outside this change.

## Commit 7: versioned archive

`POST /ingest/runtime/captures/v2` accepts a separate `RuntimeCaptureEnvelope`.
`payload` is base64; `payloadLength` and uppercase `payloadSha256` describe the
decoded original bytes. Namespace, source and epoch UUIDs are explicitly supplied;
instance/version may be unknown. Source kind, evidence stage and provenance are
labels supplied by the producer, not verification claims. Normalization must
validate them before interpreting bytes. Receipt: captureId, archived, duplicate,
normalization (`pending`). A receipt acknowledges durable evidence, not projection.

The additive `capture_sources` table records immutable metadata and an ingestion
position for v2 deliveries. Positions are ingestion order, not causal order, and
may have gaps. `raw_captures` retains the exact bytes. Same ID + same envelope is
idempotent, different metadata or bytes returns 409. Invalid metadata/hash returns
400. Malformed **payload** is still archived. The default decoded payload limit
is 16 MiB; larger HTTP deliveries return 413 without an archive acknowledgement.

Collector v1 remains the default. Explicit hook enrollment requires
`AIGENTERRA_CAPTURE_VERSION=2`, `AIGENTERRA_RUNTIME_NAMESPACE=<uuid>` and
`AIGENTERRA_SOURCE_ID=<uuid>`; optional `AIGENTERRA_RUNTIME_VERSION` is declared
metadata. Each short hook invocation creates an epoch; it does not represent a
live App Server connection. Existing v1 spool records remain deliverable together
with v2 records; v2 uses `/ingest/runtime/captures/v2` on the configured endpoint's
origin. Original oversized payloads remain on disk in `retained`, with a reason;
damaged hashes are retained as well. An outage/lost acknowledgement leaves pending
evidence for retry. Staging records after a crash remain visible as incomplete.

Migration adds tables without rewriting legacy bytes, fingerprints, outcomes,
observations, terminal statuses or evaluations. Rollback is disabling v2 producers
and continuing v1 routes/reads; do not down-migrate and delete evidence. No sources
are enabled automatically and no production database is migrated by this task.

Commit 7 verification: solution restore/build/format and 168 tests passed, plus
the isolated hook byte-fidelity probe. PostgreSQL tests used disposable databases
on the research-only loopback cluster; HTTP tests included v2 duplicate/conflict,
bad hash/length and size rejection, and the unchanged v1 recording smoke test.

## Commit 8: lifecycle interpretation

`Runtime:NormalizeCaptures=true` explicitly enables normalization after durable
v2 acceptance. Default remains archive-only (`pending`). The pinned adapter is
`codex.lifecycle/1`, tested against 0.153.4, 0.160.1 and 0.162.1 fixture bytes.
Other versions/providers/stages produce an auditable unsupported attempt. Invalid
JSON/shape produces a rejected attempt; both have zero facts and keep raw bytes.
Each invocation appends a fresh attempt. Repeating storage of an identical attempt
ID is idempotent; conflicting reuse is rejected. Attempts and their 0..N facts
commit atomically and are protected against update/delete. Legacy one-shot
`capture_outcomes` and execution aggregates remain untouched.

The typed subset is thread start/status, turn start/completion, lifecycle hooks,
and self-identifying thread history snapshots. Item/MCP correlation, agent
topology/participation, history-page request correlation and recovery policy are
later commits. In particular, an isolated turns/list response ID is insufficient
to assign a thread; such captures remain unsupported. This is not a live reader.

Facts carry capture/ordinal references, original source timestamps/units, field
locators and interpretation labels. Unknown fields remain in archived bytes.
Child hook `agent_id` owns the turn; `session_id` remains root context. Unknown
parent task assignment stays unknown. The minimal turn read use case selects an
explicit normalizer version and combines scoped facts without creating tasks or
reopening terminal legacy agents. Hook Stop is inferred completion, lower
authority than explicit runtime terminal status. Equal-authority terminal
disagreement produces TerminalUnknown + conflict with all evidence retained.
History facts are Reconciled, never live. Coverage remains Partial. No receipt
timestamp becomes an observed execution time. Unknown status is not success.

There is no background reprocessing worker, generation activation, public runtime
snapshot API or UI change in this scope. Existing captures can be interpreted via
the application use case; full replay/generation management belongs to commit 14.

Commit 8 verification: solution restore/build/format and 183 tests passed, plus
the hook probe. Tests cover old/current fixture normalization, 0..N facts, exact
byte retention, append-only attempts, transactional rollback, namespace isolation,
conflicts, explicit version selection and unchanged legacy recording APIs.
