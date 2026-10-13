# Proposed production commits 7–20 — not started

The approved execution ends at research commit 6. The sequence below is a
reviewable proposal, not authorization. **Before commit 7:** demonstrate a
supported passive live connection to both a normal CLI session and the actual
Desktop client, and obtain separate approval of this design. The current Windows
active-writer/endpoint findings do not satisfy that gate. Keep this block visible
instead of silently replacing the requirement with hooks/history polling.

The demonstration must capture one root turn, a successful/error MCP pair, child
reuse and interruption from each actual surface without resuming, taking over,
changing trust/policy, handling approvals or interrupting real user work. Use new
disposable sessions created through each normal frontend. A second observer must
connect/disconnect without altering behavior; compare baseline and observed
state, model inputs, tool outputs and owning-client usability. Record versions,
endpoint ownership, side effects, raw evidence and any gaps. If a supported route
does not exist, propose a scope decision instead of implementing a workaround.

Every later code commit runs restore/build/format/test on Aigenterra.slnx and
targeted fixture checks. Frontend changes run its formatter, build and tests.
Migration changes include legacy-database upgrade/rollback-read checks. Present
each Conventional Commit message before committing. No remotes/push by default.

| Commit / proposed message | Concrete scope and likely files | Completion criteria |
| --- | --- | --- |
| 7 `feat: archive versioned runtime evidence` | Application/Recording v2 envelope; Infrastructure raw source metadata; Api v2 capture route; Collector spool envelope support; additive migration | v1 remains byte/contract compatible; v2 archives before parse; retries idempotent; conflicting capture IDs rejected; malformed/oversize cases preserve recoverable evidence; original bytes/hash verified |
| 8 `feat: normalize runtime lifecycle evidence` | Adapters.Codex/Normalization typed lifecycle subset; Application/Runtime facts and minimal turn projection; append normalization-attempt tables | Both runtime fixtures normalize; unknown fields/events archive safely; 0..N facts per capture; child/root context distinct; existing Stop interpretation remains versioned; no provider DTO leakage |
| 9 `feat: expose runtime source health` | Codex capability detector; Application health use cases; Infrastructure health events/instances; Api health reads; opt-in doctor inventory | Enabled/untrusted/modified hooks, disconnected source and binary mismatch visible; schema/profile hashes recorded; idle != failed; doctor performs no control/config mutation |
| 10 `feat: observe supported Codex runtime connections` | Adapter transport/client split and Collector observe host; only the passive connection route proven by the pre-7 gate | Actual CLI and Desktop demonstrated; request IDs/timeouts/EOF/stderr tested; no approval routing changes; owned-child shutdown isolated; loss creates source gap; hooks keep working if App Server unavailable; no daemon requirement |
| 11 `feat: correlate MCP activity evidence` | Application activity projection and field provenance; Codex MCP normalization; Infrastructure many-to-many evidence links | Four success observations -> one call; failed result with error=null remains failed; missing post tolerated; child hook owner uses agent_id; shuffled and repeated delivery has stable IDs; two namespaces cannot collide |
| 12 `feat: project agent identity and execution segments` | RuntimeAgent/segment/link projections; subAgentActivity decoder; existing AgentExecution participation links | Three child IDs and four child turns; reused child identity stable; parent/child relation explicit; ambiguous segment ownership across parent turns stays Unknown; unknown metadata stays unknown; empty collab receiver fields create no invented relation; legacy aggregate terminal invariants preserved |
| 13 `feat: reconcile interruptions and observation gaps` | Application gap/reconciliation policy; Codex passive paginated history reader; source recovery persistence | Interrupted root has TerminalUnknown tool; interrupted child can have late completed tool without changing turn outcome; missing SubagentStop harmless to correctness; reconnect current-state recovery retains gap; no fabricated missed events |
| 14 `feat: rebuild versioned runtime projections` | Generation control, versioned readers/workers, persisted diagnostic inputs, checksum/activation transaction | Rebuild from raw evidence/facts offline equals live generation; old archived-only events can resolve later; deterministic across input permutations for explicit identities; crash-safe watermark/resume; original bytes and old generations unchanged |
| 15 `feat: add runtime snapshot and evidence APIs` | Api runtime DTO/routes; Application query ports; Infrastructure consistent snapshot readers | Generation/watermark consistent; bounded pagination; field provenance and gaps available; payload references rather than large inline bodies; octet-stream/no-store/nosniff downloads; existing timeline/evaluation endpoints unchanged |
| 16 `feat: stream durable runtime changes` | Projection change log/outbox transaction and SSE endpoint; bounded client buffers | Snapshot C + changes after C loses/duplicates no logical update; Last-Event-ID works; expired/generation-switched cursor requests reset; slow clients never block capture; outage/burst and configured memory limits tested |
| 17 `feat: add textual runtime inspection` | Web runtime route/API client and inspector built on existing execution/evidence components | Before any graph: users can inspect source health, turn/tool outcomes, stages, ownership, unknown/partial/conflict state and raw evidence; rendering remains inert; reconnection/selection tests pass |
| 18 `feat: visualize runtime entities and activities` | Angular state adapter plus focused D3 layout/edges and selection | Agent/endpoint nodes, transient activity edges; no node per MCP tool; two parallel calls and reused child render correctly; selection uses same inspector; deterministic view-state tests, keyboard and reduced-motion behavior |
| 19 `feat: show runtime lifecycle and coverage in graph` | Graph transitions, health overlays, gaps/conflicts, retention of selected details | Explicit active/failed/interrupted/unknown distinctions; fades do not mutate projection/evidence; completed child can become active again on another segment; App Server only and hooks only losses visible |
| 20 `test: validate runtime modernization acceptance` | Actual-surface opt-in acceptance suite, legacy database snapshots, self-host/Compose/Aspire docs and operational diagnostic playbook | All agreed acceptance cases pass on actual CLI and Desktop; complete .NET/frontend checks; old sessions/evaluations load; documented load/latency budgets and retention/export behavior; no process takeover; deterministic offline rebuild; operator rollback tested |

Migration rollout is additive and distributed across the commits that require
each table. New schema does not imply immediate v2 activation. Use an explicit
feature flag/generation selection; keep current ingestion and queries available
through rollout. Change architecture tests only for intended dependencies;
Domain stays independent, Api composes, AppHost orchestrates, ServiceDefaults
contains technical behavior only.

Production acceptance must distinguish:

1. Runtime turn terminal versus each tool/child terminal.
2. Explicit identities versus Unknown relation.
3. Direct live observation versus reconstructed history.
4. Complete-for-tested-capability versus partial/gapped recording.
5. Actual CLI/Desktop frontend acceptance versus executable-version tests.

Interactive replay controls, remote fleets, additional runtime providers,
output rewriting, a graph database and a custom agent runner remain out of scope.
Replay-compatible deterministic rebuild is required by commit 14; play/pause/
seek/speed UI is a separate future proposal after the live recorder is reliable.
