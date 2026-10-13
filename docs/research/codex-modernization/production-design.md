# Proposed production design — approval required

Status: **proposal only; production gate blocked**. This document belongs to
research commit 6. No production project, migration, API or UI is implemented
by these six commits. The [capability matrix](capabilities.md) and
[recovery evidence](recovery-results.md) delimit every claim below.

The requested product remains a local-first flight recorder. Its core change
is immutable multi-source evidence feeding replaceable runtime projections.
The owned stdio probe proves the protocol boundary; it does not justify taking
ownership of users' CLI/Desktop sessions. A supported passive live connection
for **both** surfaces and separate user approval are prerequisites to commit 7.

## 1. Boundaries and source architecture

```text
Codex hooks -> existing local Collector spool -------------------+
                                                               |
Supported passive App Server connection -> raw frame spool -----+
                                                               v
                 durable raw archive -> Codex normalizer (ACL)
                                      -> application facts/correlation
                                      -> versioned PostgreSQL projections
                                      -> snapshot + live changes -> UI
```

| Location | Proposed responsibility | Why this boundary |
| --- | --- | --- |
| Domain/Executions | Preserve Conversation, TaskExecution, AgentExecution and immutable Observation invariants | These describe existing business execution concepts; no Codex JSON-RPC or transport dependency belongs here |
| Application/Recording | Versioned capture envelope and archive/normalization use cases | Durable acceptance and reprocessing are application workflows |
| Application/Runtime | Runtime facts, identity resolution, deterministic activity/entity projections, gaps and health queries | Activity is an interpretation/read model, not a new aggregate needed merely by the graph |
| Adapters.Codex | Hook decoder, protocol session, capability detector, typed event subset and version-specific normalization | Provider vocabulary and protocol churn stop at this anti-corruption boundary |
| Infrastructure | Existing PostgreSQL store plus additive evidence/projection persistence and transactional change log | Storage implements use-case ports, not provider rules |
| Collector | Keep short fail-open hook capture; optional long-running observe command owns connection/spool lifetime | Hooks cannot wait on HTTP, database or UI; observation runs next to the runtime, including self-hosted API deployments |
| Api | Composition root, query/ingest endpoints, projection worker and SSE delivery | No model/session scheduling; no runtime control API |
| AppHost / ServiceDefaults | Orchestration / technical host defaults | Neither owns provider behavior or runtime semantics |

Reuse the existing IRawCaptureStore seam and specialized Application port style.
Add only ports needed by concrete use cases: append/read versioned normalization,
commit/read runtime projection changes, and query source health. Keep
IAppServerTransport internal to the Codex adapter. Do not introduce a generic
runtime plugin framework, repository per graph node, agent scheduler, external
provider abstraction hierarchy, or new graph database.

The existing collector may host an `observe codex` subcommand after approval;
its hook-capture command remains short lived. Hosting code composes the Codex
source with the generic capture sink. Transport changes do not alter Domain.
Provider-specific logs/metrics remain in Adapters.Codex and use technical
instrumentation supplied by the host.

## 2. Passive operation policy

| Operation | Proposed policy |
| --- | --- |
| initialize/initialized | Connection setup only; advertise observer identity; record negotiated profile |
| thread/read(includeTurns=false), turns/list, items/list, loaded/list | Allow after profile validation; proven history reads; rate-limit and page |
| thread/list(useStateDbOnly=true) | Candidate discovery operation; contract present, passive behavior must be verified before enabling |
| hooks/list | Read inventory; never change hashes, enablement or trust automatically |
| mcpServerStatus/list | Optional cached diagnostic query; avoid repeated large catalog reads |
| Attach/subscription operation | Default denied until same-server multi-client and disconnect side effects are verified for the exact profile |
| thread/resume/unsubscribe | Default denied for live production attachment; observed ownership behavior is unsuitable as an automatic fallback |
| thread/start, turn/start/steer/interrupt, fork/rollback/archive, config writes, approvals | Excluded from observer command surface |
| daemon start/restart/update/bootstrap/remote enablement | Never performed by observer; explicit operator lifecycle action only |
| Model-visible hook output, rewritten arguments/results, permission decisions | Never produced by monitoring hooks |

The connection must not take approval routing away from the user's client.
If an observer connection receives requests requiring runtime decisions, report
unsupported attachment semantics; do not auto-approve, reject on the user's
behalf or pretend that waiting indefinitely is harmless. Validate that closing
the observer does not terminate user work or remove another client's subscription.
Failure of either check blocks that connection profile.

No currently proven shared endpoint is prescribed as the production default.
Daemon availability must remain optional. Starting an independent stdio server
may provide scoped history reads, but cannot be presented as passive live
attachment. It must never call thread/start/resume to obtain more telemetry.
Remote transports and WebSocket implementation remain deferred until a supported
passive endpoint is evidenced; the research spike intentionally implements only stdio.

Modes are capabilities, not hidden fallbacks:

| Mode | Useful information | Explicit loss |
| --- | --- | --- |
| App Server + hooks | Runtime turn/item state, topology and hook-visible payload/trust evidence | Still no general exact model-visible result guarantee; profile gaps remain |
| App Server only | Explicit runtime lifecycle, topology, durations and available result fields | Hook evidence, hook permission context and invocation verification unavailable |
| Hooks only | Existing capture path, prompts/tools/signals, raw evidence | Interrupted-child terminals, reliable complete topology and missing-error Post coverage unavailable |
| Hooks + passive history | Above plus reconciled persisted state | Intermediate live events may never have been recorded |

All modes remain usable and labelled. Hooks-only degradation does not satisfy
the agreed two-surface live acceptance gate.

## 3. Semantic mapping and smallest useful model

| Runtime evidence | Aigenterra meaning | Rule |
| --- | --- | --- |
| Root thread | Conversation | Preserve root chat identity across turns and runtime reconnects |
| Root turn | TaskExecution | Still a useful representation of one user prompt/work cycle; terminal status is separate from every tool's outcome |
| Child thread | Stable RuntimeAgent projection | Same child can receive several turns; never equate its identity with one terminal AgentExecution aggregate |
| Child turn | RuntimeExecutionSegment projection | Scoped by child identity + turn; relates to parent task only through explicit delegation context |
| Existing AgentExecution | One agent's participation in one TaskExecution | Preserve legacy aggregate and parent invariants; link observed segments rather than reopening a finished aggregate |
| MCP server / shell runtime | RuntimeEndpoint projection | An endpoint is an entity; each call/command is an activity edge |
| Spawn, follow-up, MCP invocation, command, file change | RuntimeActivity projection | Rebuild from facts; reference all supporting evidence; no permanent node per tool |

RuntimeAgent may have several concurrent or sequential execution segments.
Completion of one segment does not delete or permanently finish the agent.
If an existing terminal AgentExecution is insufficient for a follow-up, expose
the additional segment in the runtime read model. Do not change old aggregate
semantics or invent a new root TaskExecution for a child prompt that was not
observed as a user task. A task-participation link records why a segment belongs
to a parent task; unmatched child work remains visible with Unknown task ownership.

RuntimeInstance is a read-model record of a process/service incarnation with
version, transport and capabilities. It is not an execution aggregate.
An opaque RuntimeNamespaceId identifies an enrolled local runtime environment
across restarts. This is distinct from RuntimeInstanceId and ConnectionEpochId:
thread identity must survive a process restart without merging unrelated homes.
The namespace binding is local configuration, not a plaintext username or
unscoped PID; cloned homes require an explicit binding decision.

## 4. Evidence envelope and stages

Extend capture through a new v2 envelope; keep v1 exactly supported. Proposed
fields: CaptureId, EnvelopeVersion, RuntimeNamespaceId, RuntimeInstanceId
(nullable before discovery), SourceId, ConnectionEpochId, SourceKind,
EvidenceStage, CapturedAt/ReceivedAt, SourceSequence, media type, payload length,
SHA-256 and opaque bytes. Retain existing repository-context metadata when
actually captured; do not run Git on every App Server delta.

Raw bytes are archived before parsing or normalization. A malformed or unknown
message is still durable evidence. JSON-RPC response ID correlates a request
inside one connection only; it is not a runtime activity identifier. Append
stderr separately as diagnostics, with restricted access and independent limits.

| Stage | Exact meaning |
| --- | --- |
| HookVisible | Exact bytes supplied to the hook; may contain transformed tool output |
| AppServerVisible | Exact frame bytes received from the runtime; live notification versus history response additionally recorded |
| ExternalRaw | Bytes from an independently instrumented external producer; only demonstrated for our synthetic MCP server |
| RuntimeTranscriptVisible | Selected persisted runtime transcript material; useful for research/path confirmation, not a new production source in this iteration |
| ModelVisible | Reserved for independently demonstrated model-input visibility; never assigned from a summary, hook or App Server wrapper |

Normalized facts and projected state are **interpretations**, not additional
raw stages. Each normalized field carries evidence references and a certainty/
derivation label. Full result content may differ by stage; do not manufacture
one purported original payload by combining fields from several envelopes.
Store byte fidelity and structural equality as different claims.

## 5. Time, ordering and deterministic identity

Keep current Observation.Sequence as task-local ingestion order. Add an archive
ingest position for source-wide processing and a durable projection-change
position for SSE. Neither claims global runtime causal order.

Record separately:

- OccurredAt and its meaning/precision when explicitly supplied by the source.
- SourceEmittedAt from emittedAtMs when present; preserve original numeric value/unit.
- ReceivedAt at the recorder and RecordedAt at durable acceptance.
- SourceSequence as local frame ordinal within ConnectionEpochId; hooks normally
  have no source sequence and use collector receipt metadata.
- External thread/turn/item IDs, explicit parent/cause IDs and supporting evidence.

Do not substitute ReceivedAt as an observed OccurredAt. Item millisecond fields,
turn second fields and reported durations require version-aware decoding. Clock
skew is diagnostic information; do not rewrite raw timestamps. Elapsed receipt
time is not execution duration. Display observation-order and causal views as
different views; timestamps may position items but never establish identity.

| Logical object/link | Preferred scoped identity | Supporting evidence / uncertainty |
| --- | --- | --- |
| Conversation | Namespace + root thread ID | Root hook session_id equals thread ID in tests |
| TaskExecution | Namespace + root thread ID + root turn ID | UserPromptSubmit plus explicit turn lifecycle |
| RuntimeAgent | Namespace + child thread ID | subAgentActivity.agentThreadId; child hook agent_id |
| Child segment | Namespace + child thread ID + child turn ID | Child turn/started/completed; child hook turn_id |
| MCP activity | Namespace + owning thread + turn + item/call ID | item.id equals hook tool_use_id on all tested MCP paths |
| Command/file activity | Same composite item identity | App Server identity supported by contract; hook equivalence on successful execution still needs proof |
| Delegation edge | Parent thread/turn + subAgentActivity item ID + explicit child ID | kind started/interacted identifies relation type; do not invent a missing spawn-call ID |
| Hook invocation | Capture/source delivery identity | hook run.id repeats for configured hooks; it is not globally unique and does not independently link a hook payload to an invocation |

For tool hooks, owning thread is agent_id when explicitly supplied; otherwise
session_id identifies the root context in verified cases. Preserve session_id
as conversation context separately. Require matching turn and tool ID. Missing
agent ownership is Unknown unless explicit App Server topology/IDs resolve it.
No latest-spawn, prompt-text, timestamp-nearness or label heuristic is permitted.

A parent/child identity edge is not automatically a child-segment-to-parent-turn
edge. subAgentActivity supplies the parent turn and child thread, but does not
always supply the child turn. If a child is delegated from several parent turns,
do not assign a particular child segment to the latest parent. Require a source
reference that explicitly resolves that child turn/cause; otherwise keep the
segment's task link Unknown while retaining the proven parent/child relations.
The within-one-parent-turn reuse experiment does not prove cross-parent-turn
segment assignment.

Raw retry idempotency is CaptureId plus identical envelope/payload hash. A reused
CaptureId with different content is a conflict, never an overwrite. Distinct
received frames remain distinct raw evidence even if byte-identical. Projected
activity identity is independent of arrival order. Start and terminal facts
attach to one activity; repeated equivalent facts add provenance without adding
activities. Contradictory facts are retained and surfaced, not silently deduped.

## 6. Field authority and conflicts

| Field | Preferred evidence | Fallback / conflict behavior |
| --- | --- | --- |
| Thread/turn state | Explicit App Server lifecycle; persisted history can reconcile current state | Hook Stop/Interrupt remains a signal with documented derivation; no inferred success from silence |
| Child parent/task relation | Explicit subAgentActivity parent context + agentThreadId, or populated collab IDs when verified | Preserve hook parent-session/child-ID context; no fabricated spawn invocation relation |
| Tool owner | Explicit App Server thread/turn; child hook agent_id/turn_id | Root hook session/turn for verified root paths; Unknown otherwise |
| MCP server/tool | App Server separate fields | Versioned decoder for known hook naming; retain undecodable original string |
| Arguments | App Server arguments for runtime item view | Hook tool_input as its own stage; structural mismatch recorded, no destructive overwrite |
| Result content/structuredContent/_meta | App Server result for runtime item view | Hook result available by stage; independently instrumented external raw kept separately |
| MCP failure | App Server item.status; hook isError when available | error=null is not success; absence of PostToolUse is not a successful result |
| Duration | Reported durationMs | Derived interval explicitly labelled estimate only; never hook receipt delta presented as runtime duration |
| Model | Explicit owning thread response or child-specific hook model | Never copy parent model into child as observed metadata |
| Reasoning effort | Explicit metadata for that owning thread/turn | Unknown if missing; parent/config inference labelled separately |
| Agent nickname/role/type | Explicit scoped runtime metadata | Hook agent_type retained as provider label, not assumed to equal a business role |
| Permission mode | Hook field for hook context | Effective sandbox/approval policy are distinct runtime fields; display both |
| Hook enabled/trust/hash | hooks/list effective inventory with capture time | On-disk configuration only says configured; no execution-health claim |
| Runtime binary/service version | initialize and exact executable version/hash | CLI and Desktop application versions stored separately; userAgent/source/originator do not prove frontend |
| Completeness/health | Recorder connection/capture facts + verified capability profile | No message traffic is not proof of health or failure by itself |

History may establish a terminal current state without filling the missing live
interval. Same-authority contradictory terminal facts retain Conflict=true and
both evidence sets; use TerminalUnknown when no documented rule can choose.
Hook Stop interpreted as completed never overrides explicit interrupted/failed
turn evidence. Completed child MCP after interrupted child turn is not a conflict:
these facts describe different objects.

## 7. Lifecycle, completeness and gaps

Activity status: Pending, Running, Completed, Failed, Cancelled, Interrupted,
TerminalUnknown. Only map explicit source terminal semantics. Missing terminal
after a connection ends becomes TerminalUnknown with a gap, not Completed.
An optional Abandoned reconciliation annotation means an observed start remains
unresolved after policy-driven reconciliation. It is an Aigenterra interpretation,
never a claimed Codex event or proof of cancellation.

Keep three independent dimensions: status; coverage (Unknown/Partial/
CompleteForDeclaredCapabilities); derivation (Observed/Reconciled/Inferred),
plus gap/conflict references. “Complete” always names the tested capability
profile and observation window, never implies complete model reasoning/input.
A terminal read can yield Reconciled+Partial+GapDetected at the same time.

Persist SourceGap records with source/epoch, detected time, last durable source
position, first recovered position when known, reason and estimated scope.
Reasons include disconnect, parse/oversize failure, spool capacity, hook failure,
unknown version and unfinished start. Exact lost event count is usually Unknown.
Hook capture failure can be silent by design; combine local spool health and
App Server hook-run status where available and acknowledge remaining blind spots.

After reconnect: create a new connection epoch, inspect capabilities, page only
known/authorized thread history, attach snapshot evidence, then reconcile.
Never synthesize missed item/started notifications. Preserve gaps after recovery.
Do not retry resume or daemon restart to “repair” visibility. TTL/reconciliation
decisions become recorded inputs so rebuilding never depends on current wall time.

## 8. Persistence and migration

Use additive migrations in the existing PostgreSQL context. Do not edit the five
applied migrations or the existing Completed historical records. Proposed additions:

| Storage | Purpose and essential constraint |
| --- | --- |
| capture_sources | CaptureId FK plus namespace/source/epoch/stage/sequence/version metadata; old captures remain legacy/unknown |
| normalization_attempts + runtime_facts | Append attempts keyed by capture + adapter/normalizer version; 0..N facts per capture, reason/status recorded even when unrecognized |
| runtime_instances / observation_sources | Enrolled namespace, process/service instance, capabilities and current health pointers |
| source_health_events / source_gaps | Append diagnostic history and persisted incomplete intervals |
| projection_generations | Normalizer/correlation/projection versions, input watermark, checksum and activation status |
| runtime_entities / runtime_segments / runtime_activities | Rebuildable read models partitioned by generation and namespace; unique scoped external identity |
| runtime_links / activity_evidence / field_provenance | Explicit relations and many-to-many fact/capture provenance; one call may have three, four or more observations |
| projection_changes | Durable monotonically positioned compact changes written atomically with projection state for SSE |

Names are proposed implementation targets, not a mandate to create one repository
per table. Keep raw_captures bytea as the initial original-byte store and
capture_outcomes as the immutable **legacy** one-shot receipt. New attempts do
not overwrite that receipt. Preserve evidence stored only on legacy observations;
reference it through a legacy evidence locator or copy with verified byte hash
and provenance during explicit backfill, never invent a hook capture envelope.

Migration sequence: add nullable metadata/new tables; maintain v1 writes/reads;
build v2 generation alongside existing history; verify byte hashes/counts and
legacy API snapshots; activate the v2 read model explicitly. Existing evaluations
and watermarks keep their original interpretation. New evaluations bind to a
generation. Rolling back means disabling v2 sources/reads and selecting the old
generation, not deleting raw data or reversing historical status values.

Reprocessing reads original captures plus recorded diagnostic inputs in fixed
ingest order, with pinned adapter/normalizer/correlation/projection versions.
It writes a new generation, checks deterministic results, then atomically moves
the active-generation pointer. An old archived-only capture can now normalize
after its explicit parent/turn reference becomes available. Failed attempts stay
auditable. External identity keys and per-field provenance make order-independent
correlation testable; deterministic tie-breaking orders display only.

## 9. .NET protocol client and process lifetime

Choose **typed subset + JsonExtensionData/opaque JSON**. Full generated DTO sets
would import a large changing surface; fully dynamic JSON would hide mistakes in
the small critical contract. Generate bundles only in explicit compatibility
tests, hash them, and review diffs. Keep unknown enum values/fields intact; an
unknown item is archived and marked unsupported rather than crashing ingestion.

Proposed adapter files: AppServer/IAppServerTransport.cs,
StdioAppServerTransport.cs, CodexAppServerClient.cs, Protocol/ DTO subset,
CodexCapabilityDetector.cs, Normalization/CodexEventNormalizer.cs. These names
serve concrete connection and normalization seams, not new Domain interfaces.

The transport owns asynchronous byte reads/writes, stream framing, cancellation,
stderr and process lifetime. The client owns initialize, connection-scoped
request IDs, concurrent pending requests, timeout/cancellation cleanup, separate
response/notification/server-request routing and profile checks. Archive before
dispatch, including malformed frames. Unknown notifications are retained.
Observe cancellation and duplicate/late responses without reusing IDs in an epoch.

For owned history-reader processes: close stdin, bounded graceful exit, then
terminate only that owned process tree if necessary. Never kill a user runtime.
For a proven shared transport: disconnect only the observer. Reconnect uses
bounded exponential backoff with jitter, a new epoch and history reconciliation;
it never starts work. Current Windows daemon errors yield health degradation,
not lifecycle repair. An unavailable optional source leaves hook capture working.

## 10. API contracts

Keep `/ingest/codex/captures/v1`, existing `/ingest/codex/*`, executions,
timelines, evaluations and evidence downloads compatible. Add:

| Proposed endpoint | Contract |
| --- | --- |
| POST /ingest/runtime/captures/v2 | Generic envelope + opaque bytes; archive acknowledgement includes captureId, archived=true, duplicate flag; normalization may be pending |
| GET /runtime/instances | Paged instance/source versions, surface certainty, capabilities, health and last observation |
| GET /runtime/snapshots?conversationId=... | Consistent generation + watermark + entities/segments/activities/links/gaps; bounded summary DTOs with evidence refs |
| GET /runtime/activities/{id}?generation=... | Status, correlation key, field provenance, completeness and result metadata; raw bytes fetched separately |
| GET /runtime/sources/{id}/health | Configured/enabled/trusted/connected states, capture age, errors, reconnect count, profile/schema hashes and gap refs |
| GET /runtime/evidence/{captureId}/raw | Original octet-stream attachment, no-store/nosniff; same local access controls as other evidence |
| GET /runtime/changes?after=... | SSE compact state changes, health and gaps; durable change cursor, no full tool payloads |

Initial UI read gets one consistent snapshot at cursor C and then subscribes
after C. SSE IDs bind generation and change position. Last-Event-ID resumes
within retained changes; expired cursor or generation switch returns a reset
instruction and a fresh snapshot. This reconnect protocol is for Aigenterra UI
delivery, not a claim that Codex replays its event stream. Slow clients are
disconnected/reset without delaying capture. Commit projection + change record
in one database transaction; SSE reads committed changes.

Default metadata page size 100, maximum 500; enforce payload/query budgets.
Return 409 for conflicting CaptureId reuse, 413 for ingress size limit, and
explicit pending/unsupported normalization. Preserve oversized original local
spool files with visible health; never truncate and acknowledge full capture.
No API for starting agents, approving tools, interrupting Codex or editing trust.

## 11. Health, diagnostics and compatibility

For hooks expose configured, enabled, effective trust, hash, last event, known
failure and verified coverage. “No recent event” on an idle thread is not failure.
For App Server expose connected, endpoint/transport/mode, source epoch, reported
version, schema/profile hash, last frame, last successful request, reconnect count,
archive/normalization lag and last error. Distinguish source connection health,
capture health and projection health. Version mismatch is visible, not automatically
fatal when an exact tested profile supports the subset.

Evolve these scripts into an opt-in `aigenterra codex doctor`. Default doctor is
read-only inventory: exact executable/version, features, schema hash, effective
hooks inventory, endpoint availability and profile support. An explicit experiment
mode creates disposable homes/threads and may spend model tokens; never run it
automatically on an existing user's session. Report PASS/WARN/FAIL/NOT_TESTED
per capability, binary, model and client surface, plus overall PARTIAL/BLOCKED.
Persist compact diagnostic results and sanitized fixtures, not credentials or
large tool catalogs in normal logs. A new runtime build invalidates prior
verification claims until its compatibility probe has been reviewed.

## 12. Text inspector, graph and replay

Build a textual inspector before graph animation: runtime/source identity and
health; entity/segment/activity lists; status versus completeness; provenance,
correlation IDs and evidence-stage selector; gaps/conflicts and original-byte
downloads. Reuse current execution pages and raw-evidence component behavior.
Surface inferred/unknown state plainly; never render captured HTML as executable.

Then add Angular + D3 for the expected small live graph. Angular owns application
state and selection; D3 handles layout/edges. Runtime/agents/endpoints are nodes.
Calls, delegations, commands and file changes are transient activity edges.
Do not add permanent MCP-tool nodes. Running edges may animate; success/failure
is visible before fade. Child entity remains reusable after a turn completes.
Fading/removal is view state and cannot delete evidence or change terminal status.
Unknown/partial/conflicting state uses labels/shapes as well as color; source
health remains visible. Selection shows the same inspector/provenance data.
Bound rendering updates and respect reduced motion. No million-node optimization.

Deterministic projection rebuild is in the proposed production scope. Interactive
play/pause/seek/speed controls are future work after reliable live ingestion.
Stored facts, diagnostic inputs, versions and ingest/causal metadata must permit
offline rebuild/replay without any live Codex process. Layout/animation state is
not part of domain or projection truth.

## 13. Backpressure, privacy and retention

The research measured small bursts up to 48 messages in a one-second bucket
and a 764234-byte inventory frame. It did not measure production Collector queue
depth or capacity. Do not size production solely from those averages.

Keep capture independent of UI and database availability. A bounded parser/
normalizer queue drains an append-only local spool. Limits must be explicit for
memory, frame size, disk budget and retention. On disk/oversize failure: emit a
gap/health signal where possible, keep recoverable files, fail open for hooks,
and never wait indefinitely on Codex. Existing 16 MiB capture semantics remain
until a separately tested chunk/blob upload design is needed; the spike's 64 MiB
frame limit is not automatically a production setting. Coalesce UI updates only,
never original raw evidence. Inventory caching avoids repeat 750 kB responses.

Production performance acceptance includes full hook process invocation latency
(not just its capture body), end-to-end lag, disk/DB outage, burst/concurrent calls,
queue high-water marks and a deliberately slow SSE consumer. Proposed initial
targets to validate: hook p95 <100 ms and p99 <250 ms on the target host,
no model-visible output; 100 notifications/s for 60 seconds with a 1 MiB burst
frame, no acknowledged loss and bounded configured memory. A failing budget
requires measured adjustment or implementation improvement, not hiding loss.

Retain originals unchanged in access-controlled local storage. Encrypted disk/
backup and existing self-host authentication boundaries protect prompts, paths,
tool results and account metadata. Avoid logging payloads, authorization data or
full command lines. Retention is explicit and opt-in; do not silently purge raw
evidence because normalized data exists. If an operator deletes raw content,
retain a tombstone/hash and show that evidence is unavailable; never pretend the
projection can be fully rebuilt from deleted bytes. Export redaction creates a
separate sanitized artifact with provenance and never overwrites originals.
No cloud upload, multi-tenancy, external fleets or other runtime adapter is added.

## 14. Verification obligations

Before each production code commit: restore/build/format/test Aigenterra.slnx,
relevant research/fixture tests and changed frontend checks. Update architecture
tests whenever intended project references change; no Codex/EF/transport types
may enter Domain/Application contracts. See the [phase plan](implementation-plan.md)
for concrete per-commit exit criteria.

Required production cases: the real two-version fixtures; unknown fields/enums;
byte fidelity and malformed frames; request cancellation/EOF; root versus child
ownership; failed MCP with null error; missing Post/SubagentStop; late tool result
after interrupted turn; shuffled/duplicated input; same IDs in different namespaces;
gaps/history recovery; hooks untrusted/disabled; API down/disk pressure; legacy
database upgrade; v1 contract parity; deterministic rebuild and generation switch;
snapshot/SSE resume/reset; graph fades without truth changes. Compatibility tests
that launch Codex require explicit opt-in and are not ordinary CI dependencies.

## 15. Unresolved risks and gate evidence needed

The blocking uncertainty is passive live attachment for normal CLI **and actual
Desktop**, including observation-only approval routing and safe disconnect.
No transport shim or control call may silently replace that requirement.
Dormant resume side effects, service restart/reconnect semantics, shared-client
visibility and remote operation remain unproven. Successful shell/patch coverage,
permission/compaction hooks, cancellation during hook execution, async cleanup,
large-result truncation and production load also remain unverified.

The next approval discussion should review these findings and a supported
passive endpoint demonstration. If the runtime cannot provide that route, stop
production modernization under the current acceptance agreement. A reduced
hooks/history product would require an explicit scope decision; this report
does not make that decision or start commit 7.
