# Codex runtime observability baseline — 2026-10-13

This research starts at `1bf8b0d` on `codex/runtime-observability-modernization`.
The working tree was clean. Scope is the approved six research commits only;
production modernization requires a separate decision after the research report.

## Repository and history

| Area | Current implementation / preservation requirement |
| --- | --- |
| Core | `Domain/Executions`: Conversation is a persistent chat; TaskExecution is a prompt turn; AgentExecution is participation in one task. Observation is immutable. Domain has no framework/package dependencies. |
| Use cases | Application owns specialized execution, evidence, recording and evaluation ports. Preserve `Domain <- Application <- outer adapters/hosts`. |
| Codex ACL | `Adapters.Codex/CodexHookIngestor.cs` supports eight hooks, resolves turns before tools, leaves tool agent ownership null, and does not infer parent links. Stop/SubagentStop immediately finish executions as Completed. |
| Collector | `Collector/CaptureSpool.cs` stores received bytes before forwarding; retries retain the same CaptureId. Default capture limit is 16 MiB; oversized records remain on disk. Hook transport fails open. |
| Archive | `CodexCaptureIngestor` archives before parsing. PostgreSQL `raw_captures` stores bytea plus envelope fingerprint; insert-only `capture_outcomes` stores one final interpretation per CaptureId. Archived-only out-of-order captures are not automatically reprojected. |
| Observations | Store opaque SourceEvidence, ObservedAt/RecordedAt UTC ticks and atomic Sequence. Sequence is ingestion order, not cross-source causality. Existing evidence may also exist on observation rows; preserve those bytes. |
| App Server | `scripts/Capture-CodexCommandResults.ps1` starts a bounded stdio client, initializes and performs thread/read; it projects terminal command metadata, not the complete original App Server response. No continuous subscription exists. |
| Evaluations | Deterministic command-result evaluator consumes explicit exit codes and evidence IDs; hook output text is not an exit-code channel. Preserve historical evaluations and their watermarks. |
| Persistence | Five existing migrations, including `CompleteStoppedCodexTurns` and `RecordingHardening`. Do not modify applied migrations or silently reverse historical status interpretations. |
| UI/API | Angular 22 Flight Recorder uses explicit HTTP DTOs, manual refresh, raw evidence inspection, repository context and evaluations. No live graph or live stream. |
| Hosting | Api is composition root; AppHost orchestrates; ServiceDefaults remains technical. Compose hosts Web/Api/PostgreSQL; Collector stays on the runtime machine. |

History was inspected from 2026-09-01 through HEAD, including the complete ten-commit
history. `0eb2c48` established the foundation (September 4); `b53de25`, `8a593d9`
and `3b75b92` added persistence/UI/collector tracing (September 7); `193ca6e`
added Linux self-hosting (September 8). `1bf8b0d` (October 13) adds 84-file
evaluation/recording-hardening work, including documents and migrations named
September 9/16. File names/document dates are not commit dates.

The September 8 architecture decision promotes Stop/SubagentStop to Completed.
This is a versioned interpretation to re-evaluate experimentally, not proof of
success on all current runtime paths. The existing capture receipt also cannot
represent several versioned normalization attempts or several observations per capture.

## Installed binaries and contract audit

| Fact | Observed result |
| --- | --- |
| PATH CLI (npm installation) | `codex-cli 0.153.4` |
| Desktop-bundled executable | `codex-cli 0.160.1`; a running process uses this executable |
| Desktop application version | Not inferred from its executable's CLI version |
| Running shared App Server version | Unknown; version diagnostic cannot reach a usable service |
| 0.153.4 daemon version | Rejected: daemon lifecycle is only supported on Unix platforms |
| 0.160.1 daemon version | Control-socket connection fails, Windows socket error 10050; this does not prove the daemon feature is universally unsupported |
| Relevant 0.153.4 flags | hooks, multi_agent and unified_exec enabled/stable; code_mode disabled/under development; code_mode_host enabled/stable |
| Project hooks | SessionStart, UserPromptSubmit, PreToolUse, PostToolUse, SubagentStart, SubagentStop, Stop, Interrupt; synchronous Windows launcher with two-second timeout |
| User hook file | UserPromptSubmit, PreToolUse, PostToolUse are additionally configured |
| Hook trust | Persisted hashes exist for all eight project entries; matching current hashes/effective runtime state requires hooks/list and an execution probe |

Generated ordinary and `--experimental` JSON schema bundles from each exact binary
into an OS temporary directory. Generated bundles and private runtime configuration
are not committed. Both ordinary bundles contain initialize, thread/start, resume,
read, list, loaded/list, unsubscribe, turns/list, items/list and mcpServerStatus/list.
They contain thread status/token events, turn and item lifecycle, hook lifecycle,
MCP startup and warning/configWarning notifications. Neither exposes a distinct
`thread/subscribe` method. Inclusion in an ordinary bundle is evidence of schema
presence, not a promise of stable runtime behavior or event delivery.

| Bundle | SHA-256 |
| --- | --- |
| 0.153.4 ordinary | `E8284C5CB8157554A3DD1E035AADBD4325AEA501AF56887E9C2E12EB1B9B9448` |
| 0.153.4 experimental | `B06F77062369D481A59CC70720C12B89CB9DD49C385863923262102D3AD6C978` |
| 0.160.1 ordinary | `8683F6FD9FBB994FA2BFB704AB5B02C9890238FA507A4C0FF33FF45884CD0A2E` |
| 0.160.1 experimental | `7243BA241962AF92CA60581F1A81808EBDA4212A800F8B205F54703BCFD508C5` |

Both describe MCP call IDs/server/tool/arguments/status/result/error/duration and
collab sender/receiver thread IDs. Thread originator and MCP app UI fields appear
in the inspected 0.160.1 contract but not the 0.153.4 ordinary equivalents.
Both describe twelve hook event names, more than Aigenterra currently normalizes.

The official [App Server documentation](https://learn.chatgpt.com/docs/app-server)
states that thread/read does not subscribe or resume. The generated resume contract
says it rejoins an already running thread but loads/resumes a non-running thread.
That conditional behavior must be tested in disposable sessions before production
attachment is approved. Standalone stdio success alone is insufficient.

## Verification baseline

Restore, Debug build and .NET format verification passed. Angular build, formatting
and 43 tests passed during planning. The first .NET run passed 133 tests and failed
30 PostgreSQL tests at fixture setup because Docker was unavailable and no explicit
test connection was configured. This is an environment failure, not a green suite.

Research uses a new disposable PostgreSQL 17.11 cluster on loopback and a random
port, using the already installed binaries. The existing Windows service and
application database are not test targets. The fixture creates/deletes only its
own `test_<guid>` databases. Follow-up verification is recorded below.

Follow-up on October 13: with `AIGENTERRA_TEST_POSTGRES_CONNECTION` pointing only
to that disposable cluster, **all 163 .NET tests pass**, including all 36
Infrastructure tests. No production code changed to obtain this result.

## Research evidence rules

- Original received bytes stay in a local temporary research archive, never normal logs.
- Only reviewed, sanitized copies are committed; they are not byte-identical originals.
- Missing evidence is Unknown/Not tested, never proof of absence or success.
- Probe model prompts contain only disposable synthetic work, no private repository data.
- CLI and Desktop-binary probes are separate from actual Desktop-client acceptance.
- No user daemon restart, persistent user config edit, production migration, remote or push.
