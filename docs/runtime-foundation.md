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
