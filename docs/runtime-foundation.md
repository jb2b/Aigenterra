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

## Commit 9: source health and offline doctor

`dotnet Aigenterra.Collector.dll codex doctor --executable <absolute-codex-exe>`
is an explicit inventory action. It hashes that file and runs only `--version`,
with a five-second process timeout and bounded output. It never opens an App
Server connection, calls hooks/list, starts a daemon, reads credentials, changes
trust/configuration, runs model work or sends runtime control commands. Missing
effective hooks, features, schema, connection and owner evidence stay NOT_TESTED
or Unknown. The binary path, runtime version and Desktop application version are
separate fields. Wrapper scripts are not automatically resolved/executed.

Optional inputs:

- `--schema <codex_app_server_protocol.schemas.json>` hashes an already-generated
  bundle. Doctor does not generate schemas or initialize a runtime.
- `--inventory <json>` imports a bounded (256 KiB) offline inventory in the shape
  below. Effective hooks/trust and connection state must come from a separately
  captured inventory; a config file is not proof of effective trust. The supplied
  observation time stays visible, including for stale inventories. Unspecified
  values remain unknown. Feature flags are recorded, never treated as delivery
  proof.
- `--surface cli|desktop|unknown` supplies a declared surface, never inferred from
  userAgent/source/originator. An override resets different owner evidence to
  declared. Only the operator's supplied inventory can label owner verification.
- `--record --namespace <uuid> --source <uuid> [--instance <uuid>]` explicitly
  spools a diagnostic v2 capture and adds local spool counters. Without `--record`
  doctor writes stdout only and uses temporary display IDs. It never drains or
  contacts the API. Record unknown instance as null, not a fabricated PID identity.

Representative offline input (timestamps/hashes must reflect actual evidence):

```json
{
  "version": 1,
  "observedAt": "2026-10-13T12:00:00Z",
  "surface": "cli",
  "surfaceEvidence": "declared",
  "reportedRuntimeVersion": "0.162.1",
  "connected": false,
  "reconnectCount": 1,
  "hooks": [
    { "name": "preToolUse", "configured": true, "enabled": true, "trustStatus": "untrusted" }
  ],
  "features": { "hooks": true }
}
```

Diagnostic bytes use source kind `diagnostic`, evidence stage
`RecorderDiagnostic` and provenance `inventory`. This stage is recorder-owned
metadata, never tool/model-visible evidence. The capability detector is pinned
to `codex.health/1`. Known research schema and exact binary hashes identify a
recorded profile; unknown builds, changed hashes and executable/service version
mismatches remain Unverified. Compatibility with a recorded profile does not
establish frontend ownership: a full recorded match also requires supplied
owner verification, and the recorded Desktop application version where applicable.
It does not
prove current delivery or neutrality. CLI 0.162.1 remains UNKNOWN for complete
passive live observation; actual Desktop 0.160.1 remains PASSIVE_HISTORY_ONLY
for the recorded matching profile. No profile yields PASSIVE_LIVE_SUPPORTED.
The profile hash includes detector version, surface, version, schema/binary hash
and the declared research limitations. Old 0.153.4 lifecycle fixtures remain
supported while its binary/surface health profile is unverified in this detector.

Enable `Runtime:RecordSourceHealth=true` explicitly at the API to derive health
from archived diagnostic captures. A receipt returns `health_recorded` or
`health_rejected`; in both cases the original is archived. Diagnostic processing
is separate from the optional lifecycle normalizer. Health events append with
capture/detector references. Source and instance pointers select the latest
**archive ingestion position**; delayed processing of older captures cannot
regress them. A newly received old inventory is still visibly old, not a live
measurement. Raw history is never replaced.

- `GET /runtime/sources/{sourceId}/health?runtimeNamespaceId=<uuid>` returns the
  latest diagnostic snapshot plus inventory/frame age. Enabled, untrusted and
  modified hooks, disconnected source, mismatched binary/version/schema and
  retained/incomplete spool evidence remain separate checks. Idle age does not
  imply failure. Projection health remains Unknown without independent evidence.
- `GET /runtime/instances?runtimeNamespaceId=<uuid>&after=0&limit=100` returns one
  latest diagnostic per explicitly identified runtime instance with a next
  position. Maximum page size is 500. Unknown instance IDs are omitted from this
  list and remain accessible through source health. These are bounded inventory
  reads, not the later consistent runtime snapshot/change-feed contract. Both
  read routes send `Cache-Control: no-store` and use existing local API access.

These commits add no live attachment, polling, subscription, daemon management,
automatic trust changes, agent control, SSE or frontend UI. Transport remains
commit 10 and has not started. Full MCP correlation, agent topology, recovery
gaps and projection generation management remain later approved-work candidates.

## Operational acceptance and rollback

The three migrations are additive and explicitly listed by the existing startup
migration allowlist. Production deployment is not performed here. Review/apply
migrations through the normal deployment path before enabling v2. v1 stays the
default Collector producer; set the enrollment/version variables only for
sources intentionally moved to v2. Enabling v2 alone does not populate legacy
timelines: its lifecycle read model is separate, and its future UI is outside
this scope.

Disable `Runtime:NormalizeCaptures` and `Runtime:RecordSourceHealth` and return
new hook producers to v1 for rollback. Keep all new tables/evidence and pending
v2 spool records; they remain readable/retryable without deleting or rewriting
legacy history. Do not down-migrate to roll back a producer. No production
database, runtime session, existing hook configuration or remote is changed by
the implementation task.

Commit 9 verification: solution restore/build/format and 195 tests passed, plus
the isolated hook probe. Health tests cover offline doctor options/read-only
behavior, explicit diagnostic spooling, binary/schema/owner mismatch, hooks, idle
age, disconnects, additive upgrade, append-only history, out-of-order processing,
namespace isolation and bounded HTTP queries. No frontend files changed.

A manual stdout-only doctor run against the installed CLI confirmed 0.162.1 and
the research binary/schema hashes. Compatibility remained Unverified because
frontend ownership was declared only; connection, hooks and live delivery stayed
Unknown/NOT_TESTED. The command used no recording option or network connection.
Historical research files, fixtures, Domain, AppHost and ServiceDefaults are
unchanged. Commit 10 has not started.
