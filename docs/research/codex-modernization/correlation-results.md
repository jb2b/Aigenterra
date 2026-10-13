# MCP and agent experiments

Both exact binaries were exercised through the isolated stdio client with
gpt-6-astra. MCP used a local synthetic read-only server, instead of sending
private input to an external provider. This also gives independent evidence of
the exact response bytes emitted by the MCP server. The server self-test checks
that its recorded response bytes equal stdout, including the LF delimiter.

## MCP: one success and one tool-error result per version

| Field | Hooks | App Server | Interpretation |
| --- | --- | --- | --- |
| Identity | tool_use_id | item.id | Exact equality for all four calls, scoped by session/thread and turn |
| Ownership | session_id, turn_id; no agent_id in these calls | threadId, turnId | Explicit thread/turn ownership; child ownership needs verified topology |
| Server/tool | mcp__observability_probe__echo/fail | separate server/tool fields | Prefer explicit App Server fields; adapter can decode known hook naming |
| Arguments | tool_input | arguments | Structurally equal in these cases |
| Successful result | PostToolUse.tool_response, including isError=false | result content, structuredContent, _meta; no isError | Same content/structured/meta values, different wrapper |
| Failed result | PreToolUse present; PostToolUse absent | status=failed, result contains PROBE_ERROR, error=null | Do not equate null error with success; retain failed result content |
| Duration | No duration field | durationMs | Use reported duration with its semantics, not hook receipt delta |
| Timing | No payload timestamp | emittedAtMs and item completion timestamp observed | Separate source emission/item timing from collector receipt |

For success, the complete external result object equals the hook-visible result
as JSON values. App Server keeps its content, structuredContent and _meta but
omits isError and places outcome in status. Neither complete raw message is
byte-equal to the other: envelopes and serialization differ. The external-server
JSON-RPC envelope is a third representation. Exact model-consumed equality is
**Unknown**; the model's summary is not independent proof of its complete input.

The two failed calls emitted no PostToolUse despite actually executing the MCP
server. This reproduces missing terminal hook coverage for this tool-error path
on both versions. There is still a terminal App Server item. The successful
calls have four evidence records; failed calls have three. A projection must
create one activity by explicit ID, not require exactly four observations.

The mcpServerStatus/list response includes a large runtime-provided codex_apps
tool catalog. Whole-stream sizes (811,063 / 819,250 bytes) are therefore not MCP
result sizes. Measure inventory responses separately from live activity events.

## Agents: initial child, follow-up, two parallel children

Both runs produced three distinct child IDs, three SubagentStart hooks and four
SubagentStop hooks. One child receives two turns under the same stable agent ID.
The two parallel children have overlapping observed start/completion intervals
on both versions; this describes timing, not how relationships are identified.

The useful representation is **subAgentActivity**, present in both generated
ordinary schemas and actual streams. Its kinds here are started, interacted
and completed. Its explicit agentThreadId identifies the child; the surrounding
notification's threadId/turnId identifies the parent context. agentPath provides
a label, not identity. Eight completed activity items describe three starts,
one follow-up and four completions. CollabAgentToolCall items in these runs are
wait calls with empty receiverThreadIds and agentsStates; those fields cannot
be used to invent child relationships.

Subagent hooks use the **parent session_id but child turn_id**, with agent_id
equal to the explicit subAgentActivity.agentThreadId. There is only one
UserPromptSubmit in each parent run, not a prompt hook for every child turn.
The current ingestor's turn-first lookup therefore cannot reliably attach these
child hooks to the parent TaskExecution. This is a concrete modernization gap.

Child completion means one child turn completed, not that the child identity is
permanently finished. Existing terminal AgentExecution aggregates cannot simply
be reopened for a follow-up. Preserve a stable runtime-agent projection plus
separate observed execution segments/turns; map task participation using explicit
parent context. Do not infer it from latest spawn, label, prompt or timestamp.

Child model/effort/nickname are not populated by the useful events inspected
here. Parent model/effort must not be copied into those fields as observed child
metadata. Keep them Unknown until a separate explicit source supplies them.

The root thread source is reported as vscode and (on 0.160.1) originator as
Codex Desktop in this custom stdio probe. Preserve those reported fields, but
do not use them as independent proof of which frontend actually launched a run.

## Regression evidence

Jointly sanitized fixtures include both streams and synthetic external MCP
responses. Golden tests assert ID equality, argument/result fidelity, failed
state with null error, the absent failed-call post hook, three explicit child
identities and two distinct turns for the reused child. Original bytes remain
local; synthetic markers are used only to compare our known test-server output,
never as a production correlation heuristic.
