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
