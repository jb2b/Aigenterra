# Hook experiments

Measured on Windows on 2026-10-13. All workspaces/homes were disposable. The
production hook configuration, hashes and existing sessions were not changed.
Fixtures live under `research/codex/fixtures/<version>/hooks-*`.

| Experiment | Expected | Observed |
| --- | --- | --- |
| 0.153.4 + gpt-6.1-sol | Bounded shell/patch turn | Account/runtime model request rejected (HTTP 400); SessionStart, UserPromptSubmit, SessionEnd received. No claim of successful tool coverage. |
| 0.153.4 + gpt-6-astra | Read-only Get-Location call | CLI exits 0; tool launch is rejected by policy. SessionStart, UserPromptSubmit, PreToolUse(Bash), Stop, SessionEnd received. No PostToolUse; this is not a successful-tool coverage test. |
| 0.160.1 + gpt-6.1-sol | Shell and harmless apply_patch | Both actions rejected by effective tool policy/read-only sandbox. SessionStart, UserPromptSubmit, PreToolUse(Bash), PreToolUse(apply_patch), Stop, SessionEnd received; no PostToolUse. This does not reproduce a missing-post bug for executed tools. |
| 0.160.1 untrusted hooks | No hook execution | hooks/list reports all twelve enabled but untrusted; model-only turn exits 0, zero hook files. |
| Persisted trust/restart | Trust survives process boundary | Both versions list all twelve hooks as trusted after storing hashes obtained from hooks/list in the isolated config and starting another process. |
| Hook byte fidelity/failure | Exact input, empty stdout, exit 0 | Standalone test verifies UTF-8 multibyte/CRLF fidelity, spaces in paths and storage failure; all pass. |

The 0.153.4 catalog offers gpt-6-astra but did not offer gpt-6.1-sol during this
run. Repeating with an advertised model avoids misclassifying model compatibility
as hook coverage. Version and model must both be captured in diagnostics.

## Received fields

| Event | Fields beyond event name |
| --- | --- |
| SessionStart | session_id, transcript_path, cwd, model, permission_mode, source |
| SessionEnd | session_id, transcript_path, cwd, reason; no turn_id/model/permission_mode in these samples |
| UserPromptSubmit | session_id, turn_id, transcript_path, cwd, model, permission_mode, prompt |
| PreToolUse | session_id, turn_id, transcript_path, cwd, model, permission_mode, tool_name, tool_input, tool_use_id |
| Stop | session_id, turn_id, transcript_path, cwd, model, permission_mode, stop_hook_active, last_assistant_message |

These payloads contain no source timestamp. Recorder receipt time is separate
metadata. tool_use_id values in these probes use an `exec-<uuid>` form; do not
assume historical fixture call-ID formats. The received stage is HookVisible,
not proven external MCP output or exact model-visible output.

SessionEnd is a useful raw observation currently not normalized by Aigenterra.
Stop also occurred after denied tool attempts, so Completed-turn and
Succeeded-tool must never be conflated. Current Stop/SubagentStop status rules
require an explicit interpretation-version boundary.

The CLI was requested to use workspace-write, but the inspected 0.160.1 turn
context reports read-only with approval policy never. Hooks nevertheless report
permission_mode=bypassPermissions. That field cannot be treated as the effective
filesystem sandbox. The experiments did not escalate or bypass the rejection.

Verification: the recorder byte/failure test passes. The existing recording
smoke test intermittently fails at its branch assertion (expected first, actual
null); its isolated rerun passes. Git context has an existing 500 ms deadline.
The research check script serializes test projects to reduce load; concurrency
inside tests is preserved. A trial with one xUnit thread also passed all 163
tests but took longer; that setting is not required. Production timeouts and
tests are not weakened or changed.

PermissionRequest, compaction, subagent hooks, successful PostToolUse, Code Mode,
parallel tools and live config reload are not established by these first probes.
Later protocol experiments supplement this report; absence here is not a runtime
capability claim. No expensive context-filling compaction experiment is planned.
