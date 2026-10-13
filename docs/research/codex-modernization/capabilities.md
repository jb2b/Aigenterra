# Capability and historical-gap matrices

Evidence date: 2026-10-13, Windows. Versions are exact runtime executables,
not Desktop application versions. **Observed** means the named controlled
path, not universal support. **Contract** means present in the generated ordinary
bundle; this is not a stability guarantee. CLI help labels App Server experimental.
Both probes initialized with experimentalApi=true, so ordinary-schema inclusion
does not prove successful negotiation with that flag disabled.

## Runtime surfaces

| Capability | CLI binary 0.153.4 | Desktop binary 0.160.1 in isolated harness | Actual Desktop frontend |
| --- | --- | --- | --- |
| Harmless CLI lifecycle hooks | Observed | Observed via exec | Not exercised |
| Owned stdio thread/turn | Observed | Observed | Not an acceptance substitute |
| MCP success/error, explicit identity | Observed | Observed | Unknown |
| Three children, parallel children, reused child | Observed | Observed | Unknown |
| Root and child interruption | Observed with gaps | Observed with gaps | Unknown |
| Code Mode concurrent MCP items | Observed | Observed | Unknown |
| Passive history read across processes | Observed | Observed | Endpoint availability not proven |
| Passive live attachment to another process | Not established; read receives no lifecycle events | Same | Not established |
| Daemon version | Unix-only lifecycle rejection | Control-socket error 10050 | Running service version Unknown |
| Version mismatch diagnostic | Two installed binaries differ | Initialize confirms executable version | App version kept separate/Unknown |
| Daemon reconnect/restart/multi-client | Not tested | Not tested | Not tested |

The owned stdio process is a viable research harness and potential passive
history-reader fallback. It is not a production agent runner or a proven live
source for an independently running user session.

## Hooks

All twelve event names appear in both installed inventories. The same isolated
entries can be enabled/trusted, enabled/untrusted or modified. Configuration
inventory is not evidence that a particular execution path emits an event.

| Hook | Actual coverage in these probes | Fields / limitation |
| --- | --- | --- |
| SessionStart | Both versions, startup | session_id, model, permission_mode, cwd, transcript_path, source; no turn ID in hook payload |
| SessionEnd | Both versions, process exit | session_id, cwd, transcript_path, reason; no turn/model/permission fields in samples |
| UserPromptSubmit | Both versions, root prompts | session_id, turn_id, model, permission_mode, prompt; absent for child turns tested |
| PreToolUse | Both: MCP, local multi-agent functions, Code Mode nested MCP; denied shell; denied patch on .160 | tool_use_id, tool_name, tool_input and root context; child MCP adds agent_id/type |
| PostToolUse | Both: successful MCP, local multi-agent functions, Code Mode nested MCP, late child MCP | tool_response; absent for executed MCP isError result and interrupted root call |
| PermissionRequest | Contract/inventory only | Not triggered under approvalPolicy=never |
| PreCompact | Contract/inventory only | Not triggered; no artificial context flooding |
| PostCompact | Contract/inventory only | Not triggered |
| SubagentStart | Both: three distinct child identities | parent session_id, child turn_id, agent_id, agent_type plus common context |
| SubagentStop | Both: normal child turns; reused child stops twice | Missing in both interrupted-child runs; one event is not an agent lifetime |
| Stop | Both: normal root turn, including denied tools | last_assistant_message, stop_hook_active; does not prove all tools succeeded |
| Interrupt | Both: interrupted root | Absent for interrupted child in these samples |

Common context when present: cwd, transcript_path, model, permission_mode.
No source timestamp was present in the inspected hook payloads. Capture receipt
metadata is separate. Hook permission_mode=bypassPermissions conflicted with
the effective read-only tool policy; these are distinct fields, not interchangeable.
Child MCP hooks do provide their own model; child reasoning effort remains Unknown.

| Tool path | Pre/Post conclusion |
| --- | --- |
| Shell/Bash/unified exec | Pre observed before policy denial; successful Post coverage Unknown |
| apply_patch | .160 Pre observed before policy denial; successful edit/output coverage Unknown |
| MCP direct | Success has Pre+Post; executed tool error has Pre only |
| Code Mode | Nested MCP Pre+Post observed; outer exec/batch coverage absent in selected streams |
| spawn/follow-up/wait local functions | Pre+Post observed; explicit child relationship additionally supplied by App Server |
| Parallel MCP | Separate IDs; both starts precede first completion; server itself serializes requests |
| Trust/restart | Persisted isolated hashes survive restart; untrusted configuration emits no hook files |
| Live reload | hooks/list detects command modification and restored trust without restarting |
| Async hooks/cleanup | Not tested; probes intentionally synchronous and silent |

## App Server contract and observation

| Method/event/data | 0.153.4 / 0.160.1 contract | Actual observation and production classification |
| --- | --- | --- |
| initialize, initialized | Ordinary | Both handshake successfully; initialize userAgent version retained |
| thread/start | Ordinary | Both; creates runtime work context, prohibited for production observer |
| thread/resume | Ordinary | Active-writer rejection across separate processes; mutating/conditional attachment, prohibited by default |
| thread/read | Ordinary | Passive metadata read works; includeTurns=true warns to use pagination |
| thread/turns/list, thread/items/list | Ordinary | Both recover persisted turns/items without loading thread |
| thread/list | Ordinary | Contract inspected, not invoked in this controlled sequence; prefer state-DB-only option when supported to avoid metadata repair |
| thread/loaded/list | Ordinary | Both report only own server's loaded threads; not machine-wide discovery |
| thread/subscribe | Absent in inspected bundles | No standalone subscription method to assume |
| thread/unsubscribe | Ordinary | notLoaded on observer; owner's unsubscribe leaves thread loaded in this test; not automatically passive |
| thread/started, status/changed | Ordinary | Both observed for owned work |
| thread/tokenUsage/updated | Ordinary | Both observed; token counts are provider metadata, not billing attribution |
| turn/started, turn/completed | Ordinary | Both, including child interrupted and parent completed as separate outcomes |
| turn/interrupt | Ordinary | Both controlled cases; runtime control, prohibited for production observer |
| turn/plan/updated, turn/diff/updated | Ordinary | Contract only; no successful edit or plan delta demonstrated |
| item/started, item/completed | Ordinary | Both; terminal may be missing or later than containing turn terminal |
| commandExecution, fileChange | Ordinary item kinds | Successful execution/change not proven by these App Server probes |
| mcpToolCall | Ordinary item kind | Both: IDs, server/tool/arguments, status, result, duration; error may be null on failure |
| collabAgentToolCall | Ordinary item kind | Wait operations observed; receiverThreadIds and agentsStates empty in topology cases |
| subAgentActivity | Ordinary item kind | Both: agentThreadId plus parent notification context; started/interacted/completed |
| hook/started, hook/completed | Ordinary | Both; run.id resembles configured hook identity and repeats, not a globally unique invocation ID |
| hooks/list | Ordinary | Both; twelve hooks, enable/trust/hash details and live inventory reload |
| mcpServerStatus/list | Ordinary | Both; large catalog response, not live MCP result size |
| mcpServer/startupStatus/updated | Ordinary | Both; server startup/ready observed |
| warning, configWarning | Ordinary | Contract only in the exercised successful paths |
| deprecationNotice | Ordinary | Both observed for includeTurns=true |
| Thread.parentThreadId, agentNickname, agentRole | Ordinary | Contract exists; populated parent linkage comes from subAgentActivity here; don't invent absent labels |
| Model/reasoningEffort | Ordinary metadata | Root response/config observed; child model from child hook only; child effort Unknown |
| Thread.originator | Not in .153 ordinary / in .160 ordinary | .160 reports Codex Desktop in custom harness; not verified frontend identity |
| emittedAtMs | Observed notification envelope | Emission time, separate from item timestamps and recorder receipt |

## Historical limitations: bounded classifications

| Historical concern | Classification | Evidence / limit |
| --- | --- | --- |
| Incomplete specialized Pre/Post coverage | Reproduced | Executed isError MCP has no Post; cancelled root MCP has no terminal item or Post |
| Code Mode drops all nested MCP hooks | Not reproduced on this path | Two nested successful MCP calls have both hooks on both versions; other tools Unknown |
| No batch-level PostToolBatch | Reproduced in inspected surface | Twelve hook names contain no batch event; no outer exec hook/item observed |
| Hook start without end after cancellation | Not reproduced in these runs | Counts match; interrupting a hook itself and async cleanup are Not tested |
| Interrupted child lacks SubagentStop | Reproduced | Explicit interrupted child turn, no SubagentStop in either version |
| Hook-only spawn-to-child correlation incomplete | Reproduced as a limitation | Child agent ID exists, but no general spawn-call-to-child cross-ID; App Server supplies explicit parent/child context |
| PostToolUse cannot generally replace model-visible result | Unknown / intentionally not tested | Passive probes output nothing; result-rewrite capability is outside scope |
| CLI and Desktop differ | Unknown for actual frontend | Binary probes agree on tested cases; no actual Desktop acceptance run |
| Trust affects discovery/execution | Reproduced | Enabled/untrusted hooks emit no files; modified command invalidates trusted hash |
| Windows daemon/process edge cases | Reproduced narrowly | Version diagnostics fail as described; no broad Windows support conclusion |
| Async hook cleanup | Not tested | No asynchronous hook experiment |

No global historical bug is labelled Fixed on the strength of one controlled
sample. Unknown capabilities remain visible in the proposed diagnostic profile.
