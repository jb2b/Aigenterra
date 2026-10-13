# Representative sanitized payloads

These are **field selections** from actual 0.160.1 fixtures, not complete wire
frames or byte-identical originals. IDs are consistent inside each fixture set;
do not correlate identities across separately sanitized scenarios. Complete
selected payloads and original-stream hashes are linked below.

## One MCP call, multiple representations

From [mcp-comparison/events.json](../../../research/codex/fixtures/0.160.1/mcp-comparison/events.json):

```json
{
  "hook_event_name": "PreToolUse",
  "session_id": "00000000-0000-4000-8000-000000000001",
  "turn_id": "00000000-0000-4000-8000-000000000002",
  "tool_use_id": "exec-00000000-0000-4000-8000-000000000004",
  "tool_name": "mcp__observability_probe__echo",
  "tool_input": { "marker": "MCP_COMMIT4_OK", "size": 32 }
}
```

The completed App Server item's selected fields are:

```json
{
  "type": "mcpToolCall",
  "id": "exec-00000000-0000-4000-8000-000000000004",
  "server": "observability_probe",
  "tool": "echo",
  "status": "completed",
  "arguments": { "marker": "MCP_COMMIT4_OK", "size": 32 },
  "result": {
    "structuredContent": {
      "result": "PROBE_OK",
      "marker": "MCP_COMMIT4_OK",
      "padding": "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx"
    },
    "_meta": { "origin": "synthetic-server", "evidenceVersion": 1 }
  },
  "error": null,
  "durationMs": 13
}
```

The full fixture also retains result.content. Matching PostToolUse.tool_response
and the external MCP response include isError=false; the App Server result omits
isError. The envelope's threadId/turnId match the hook session/turn for this root
call. Four observations support one logical activity, rather than four activities.

For the executed failing call in that same fixture, selected fields are:

```json
{
  "id": "exec-00000000-0000-4000-8000-000000000005",
  "tool": "fail",
  "status": "failed",
  "result": {
    "structuredContent": {
      "result": "PROBE_ERROR",
      "marker": "MCP_COMMIT4_ERROR",
      "padding": ""
    }
  },
  "error": null,
  "durationMs": 6
}
```

External MCP isError=true is present; PostToolUse is absent. Null error is not
success, and the result remains useful failure evidence.

## Explicit parent/child relation

From [agent-topology/events.json](../../../research/codex/fixtures/0.160.1/agent-topology/events.json):

```json
{
  "method": "item/completed",
  "params": {
    "item": {
      "type": "subAgentActivity",
      "id": "call_c3f5ffcef72a4bb8a7a275088e45601f",
      "kind": "started",
      "agentThreadId": "00000000-0000-4000-8000-000000000006",
      "agentPath": "/root/child_one"
    },
    "threadId": "00000000-0000-4000-8000-000000000001",
    "turnId": "00000000-0000-4000-8000-000000000004",
    "completedAtMs": 1791898773784
  },
  "emittedAtMs": 1791898773793
}
```

The parent context is explicit in the notification. `kind=started` refers to
the child activity despite the outer item/completed event. Event method alone
cannot determine whether the child finished. agentPath is a label; agentThreadId
is identity. The same child later receives an interacted item and a second turn.

## Interrupted child with a later tool result

From [interrupt-child/events.json](../../../research/codex/fixtures/0.160.1/interrupt-child/events.json),
the child PreToolUse contains:

```json
{
  "session_id": "00000000-0000-4000-8000-000000000004",
  "turn_id": "00000000-0000-4000-8000-000000000002",
  "agent_id": "00000000-0000-4000-8000-000000000001",
  "agent_type": "default",
  "model": "gpt-6-astra",
  "hook_event_name": "PreToolUse",
  "tool_use_id": "exec-00000000-0000-4000-8000-000000000003"
}
```

The App Server turn for child `...001` is interrupted at emittedAtMs
1791899818007. Its MCP item completes at 1791899835992. Hook session_id refers
to parent `...004`, while agent_id supplies the actual tool owner. No SubagentStop
appears. These are separate turn/tool outcomes, not a timestamp-based relation.

## Recovered state without live subscription

From [recovery/events.json](../../../research/codex/fixtures/0.160.1/recovery/events.json),
derived probe diagnostics record:

```json
{
  "observerLifecycleNotifications": 0,
  "observerLifecycleNotificationsAfterReconnect": 0,
  "resumeSucceeded": false,
  "resumeError": {
    "code": -32600,
    "message": "thread 00000000-0000-4000-8000-000000000001 already has an active writer"
  }
}
```

Two turns were read before disconnect, three after. The diagnostic is a probe
summary, not a runtime event claiming replay. See the complete diagnostics for
the persisted turn identities and statuses.

## Fixture inventory

Both `0.153.4` and `0.160.1` directories contain stdio-smoke, mcp-comparison,
agent-topology, interrupt-tool, interrupt-child, recovery and code-mode scenarios,
plus their relevant CLI hook fixtures. Code Mode includes selected exec tool-call
records demonstrating Promise.all/Promise.allSettled and two per-call hook pairs.
Manifests give source hash/size, selected event/hook counts and sanitized stages.
Original stream bytes, observer connection archives, transcript files and schema
bundles are retained in the private local research archive described in the report.
