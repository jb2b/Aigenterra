# Runtime observability modernization — research delivery

The six authorized research commits are complete with this document. **Commit 7
has not started.** Production remains blocked on a proven passive live observation
route for both normal CLI and actual Desktop, followed by separate approval.
No production source, main solution dependency, migration, API or UI changed.

## Findings that determine the design

1. Installed runtimes differ: npm CLI **0.153.4**, Desktop-bundled executable
   **0.160.1**. Both owned stdio probes initialize and expose useful lifecycle,
   MCP, agent, hook and token events. Their reported “Codex Desktop” userAgent
   prefix does not establish frontend identity.
2. MCP item.id equals hook tool_use_id in the tested paths. Successful calls
   preserve content/structuredContent/_meta across the three measured stages,
   but wrappers differ. App Server omits isError. An executed failing MCP call
   reports status=failed with error=null and no PostToolUse. One call therefore
   has a variable number of evidence records, not a fixed quartet.
3. Explicit subAgentActivity.agentThreadId plus parent notification context
   establishes topology. A child receives follow-up work under the same ID:
   three children produce four turns/stops. Stable agent identity and execution
   segments must be separate. Child hooks combine parent session_id with child
   turn_id; child tool hooks additionally supply the actual agent_id.
4. Root interruption has an explicit interrupted turn but no closing MCP item.
   Child interruption lacks SubagentStop and later receives a completed MCP
   result. Tool success cannot overwrite an interrupted containing turn.
5. Code Mode's two concurrently outstanding MCP calls each emit Pre/Post hooks.
   The outer exec/batch does not have an equivalent event in these streams.
   Hook inventory reload detects modified hashes without process restart.
6. Independent stdio observers can read persisted history, including a turn
   missed while disconnected, but receive no live lifecycle events from the
   owning process. Resume fails with “already has an active writer”; owner
   unsubscribe leaves the thread loaded. This is a concrete limit on the proposed
   live attachment, not an implementation detail to conceal with polling.

## Evidence and repository baseline

Read [baseline](baseline.md) for project responsibilities, ten-commit history
through `1bf8b0d`, preserved behavior, installed versions, four schema hashes and
initial test setup. The material October 13 change includes recording hardening
and evaluations; migration/document filenames dated September 9/16 are not their
Git commit dates. The existing adapter handles eight hooks, archives before
parsing, resolves turns before tools, lacks a continuous App Server source and
has one final outcome per capture. Collector raw spool/fail-open behavior,
historical evidence, task-local sequence and deterministic evaluations must remain.

| Current gap | Evidence | Proposed resolution |
| --- | --- | --- |
| Child turn lookup expects an already known task turn | Child hook turn differs from parent; no child UserPromptSubmit | Explicit child segment + parent task link; reprocessable unresolved facts |
| Terminal AgentExecution treated as one child lifetime | Reused child has two turns | Stable agent read model, multiple segments; preserve legacy aggregate semantics |
| Stop/SubagentStop interpreted immediately as Completed | Missing child stop on interrupt, independent tool failure | Versioned interpretation and authoritative scoped lifecycle facts |
| Hook-only tools have incomplete terminals | Failed MCP has no post; root interruption has no terminal item | App Server evidence when available; explicit TerminalUnknown/gap when not |
| Raw receipt is one-shot and may remain archived-only | Existing capture_outcomes structure | Append normalization attempts and versioned rebuildable projections |
| Sequence could be mistaken for execution order | Two sources and late child tool result | Separate ingest/source/causal order and timestamp semantics |
| No runtime source-health/capability view | Trust/daemon/version/connection differences | Effective inventory, source epochs, health/gaps and doctor profile |
| Current history capture uses full thread/read hydration | Both binaries emit deprecation notice | Capability-aware paginated passive history reads |
| Live graph lacks a reliable external live source | Separate process receives no owner notifications | Block production until actual-surface passive route is proven |

Detailed experiment reports: [hooks](hook-results.md), [stdio](appserver-results.md),
[MCP/agents](correlation-results.md), [passive/recovery/performance](recovery-results.md).
See [capability matrices](capabilities.md) for schema-versus-observation and
Fixed/Reproduced/Not reproduced/Unknown/Not tested classifications. No historical
bug is globally declared fixed from this sample.

## Proposed decision

Adopt the [production design](production-design.md) as a **conditional proposal**:
existing fail-open Collector plus a supported passive runtime source; immutable
raw evidence; Codex normalization inside its adapter; vendor-neutral Application
facts, runtime-agent/segment/activity projections; additive PostgreSQL generations;
snapshot/SSE API; textual inspector followed by an Angular/D3 live graph.

Activity belongs in the read model, not a new Domain aggregate. Explicit scoped
identifiers correlate evidence; timestamps never establish parentage. Preserve
field-level provenance/conflicts and separate tool/turn status from completeness.
Keep hooks-only and App Server-only modes useful while displaying their losses.
Deterministic offline rebuild is required; interactive replay UI remains deferred.

The [proposed commits 7–20](implementation-plan.md) name files/use cases and
acceptance criteria for each step. They are **not being executed**. Starting an
owned model thread to obtain telemetry, taking over with resume, daemon restart
or substituting hooks/history polling would not meet the passive live agreement.

## Payloads, archives and reproducibility

[Representative payloads](evidence-examples.md) show exact selected fields and
link to real sanitized fixtures under `research/codex/fixtures/<version>/`.
Fixtures jointly pseudonymize UUIDs and paths across related sources. Account,
reasoning and unrelated response payloads are excluded. Original-byte hashes
are in fixture manifests. Sanitized copies are not byte-identical originals.

The private archive is at
`%USERPROFILE%\.codex\artifacts\aigenterra-runtime-research-20261013`.
It preserves the four disposable workspaces' original evidence, transcripts and
the generated schema audit, outside Git. The original temporary directories
also remain. Copied authentication files were removed after every experiment;
they are not part of the archive. Treat original stderr/transcripts/account
notifications as private data. No external upload or push was performed.

[Research README](../../../research/codex/README.md) documents setup and reruns.
Use exact executable paths, an advertised supported model and a fresh disposable
home. The 0.153.4 request for gpt-6.1-sol failed with HTTP 400; gpt-6-astra worked.
Do not confuse model compatibility with hook coverage. Shell/apply_patch probes
encountered effective-policy denial; no successful edit/command claim is made
and the denial was not bypassed. The synthetic read-only MCP server provided
controlled independent external bytes without using private repository input.

## Verification and limitations

| Check | Result |
| --- | --- |
| Main solution restore/build | Passed; zero build warnings/errors |
| Main solution dotnet format --verify-no-changes | Passed |
| Main .NET tests | **163 passed**: Application 44, Architecture 11, Codex 40, Domain 32, Infrastructure 36 |
| Separate research solution build/format | Passed |
| Research .NET tests | **18 passed**: four transport tests and fourteen two-version fixture cases |
| Hook capture self-test | Passed: exact UTF-8/CRLF bytes, paths with spaces, silent success and silent storage failure |
| Synthetic MCP self-test | Passed: archived response bytes equal stdout, success/isError semantics |
| Angular baseline | Build/format and **43 tests passed** during baseline; frontend unchanged by these commits |
| Database isolation | Disposable PostgreSQL 17 cluster on loopback; stopped after verification; existing Windows service/application database untouched |
| Dependency/production scope | No production project reference or source change; research solution remains separate |
| Archive provenance | All 14 App Server fixture source hashes match preserved original files; private archive has no auth.json |

The initial database-less test run failed at setup; it is not counted as passing.
An existing worktree branch-detection test intermittently failed under load and
passed in isolation; serial test-project execution then passed all 163. Production
tests/timeouts were not weakened. One research build attempted while its Windows
apphost was executing failed with a file lock; build succeeded after the probes
finished. These failed attempts are documented rather than hidden.

Unproven: actual Desktop and normal-CLI passive live attachment; shared service
restart/reconnect and approval routing; dormant-thread resume side effects;
successful shell/patch coverage in this probe; permission/compaction hooks;
async hook cleanup; interrupted hook execution; large-result/model-visible
completeness; sustained throughput and Collector queue depth. Small-sample
measurements are in [recovery results](recovery-results.md). Missing evidence is
not success, absence or universal lack of support.

## Delivery traceability

| Requested output | Location |
| --- | --- |
| 1–3 Current state, history, installed versions | baseline.md; report findings |
| 4–6 Runtime/hook/App Server matrices | capabilities.md |
| 7–9 Experiments, payloads, historical limitations | Four experiment reports; evidence-examples.md; capabilities.md |
| 10 Current-code gaps | Report gap table |
| 11–14 Sources, passive guarantees, stages, time/causality | production-design.md sections 1–5 |
| 15–17 Semantic mapping, neutral model, Activity decision | production-design.md section 3 |
| 18–20 Correlation, field authority, completeness | production-design.md sections 5–7 |
| 21–25 Reprocessing, health, doctor, migration, .NET client | production-design.md sections 8–11 |
| 26–30 Files, API, inspector, graph, replay | production-design.md sections 1/9/10/12; implementation-plan.md |
| 31–36 Tests, performance, privacy, compatibility, risks, assumptions | production-design.md sections 8/13–15; recovery-results.md |
| 37–38 Phased order and completion criteria | implementation-plan.md |

Commits preceding this sixth documentation commit:
`de0fae2` baseline; `4e7cda5` hook probes; `58a4569` stdio probe;
`0c1f722` MCP/agent evidence; `67703c8` passive observation/recovery.
The sixth commit is `docs: specify evidence-driven runtime modernization`;
its hash is provided with the delivery. No commit 7 or production implementation
is included.
