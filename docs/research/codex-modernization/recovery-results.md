# Passive observation, cancellation and recovery

## Acceptance gate: BLOCKED for production

Both Windows binaries (0.153.4 and 0.160.1) passed an isolated owner/observer
experiment. They did **not** prove passive live attachment to normal CLI or
actual Desktop sessions. Running the Desktop binary under our stdio harness
does not constitute Desktop acceptance. No existing user session was resumed,
interrupted, unsubscribed, restarted or reconfigured.

The second process shared only the disposable CODEX_HOME with the first.
It initialized, read the owner's thread, remained connected during another
turn, disconnected, missed a third turn, then read history again.

| Operation | Both versions: observed result | Meaning |
| --- | --- | --- |
| thread/read, includeTurns=false | Succeeds; observer loaded list stays empty | Read does not load or subscribe in this test |
| Owner runs while observer connected | Zero observer lifecycle notifications for that thread | Independent stdio is not a shared live subscription |
| thread/turns/list | Two terminal turns while connected; three after reconnect | Persisted state recovery works; missed notifications were not replayed |
| thread/items/list | Six persisted items after three model-only turns | History API usable without resume |
| Observer thread/resume | -32600, `already has an active writer` | Cannot attach a second writer to the owner's thread |
| Observer thread/unsubscribe | notLoaded | No subscribed thread in that observer |
| Owner thread/unsubscribe | unsubscribed, but owner loaded list still contains thread after one second | Unsubscribe does not demonstrate unloading/releasing ownership |
| Another resume after owner unsubscribe | Same active-writer rejection | Unsubscribe is not a workaround for ownership |

The early raw summary calls the second resume `offlineResume`; the owner was
in fact still loaded. Exported diagnostics and the repeatable probe use the
corrected name `resumeAfterUnsubscribe`. Original raw summaries are unchanged.
Dormant-thread resume, same-server multiple clients, owner crash/restart,
daemon restart and missed-event retention remain **unproven**.

The installed CLI help calls app-server experimental. Both versions offer stdio,
Unix sockets, WebSocket listeners, proxy and daemon management. Proxy forwards
stdio to an existing Unix control socket; it does not turn an arbitrary running
stdio process into a shared service. 0.153.4 rejects daemon lifecycle on Windows;
0.160.1 cannot connect to this machine's control socket (10050). A read-only
process check found the actual Desktop binary running app-server without an
explicit listen/proxy option; help says stdio is the default. This supports the
endpoint limitation, but is not proof of every possible Desktop transport.
No WebSocket/remote implementation, daemon bootstrap or user-process restart
was attempted. Remote and version-mismatch negotiation behavior is Unknown.

Production must remain gated until a supported, side-effect-tested observation
route is demonstrated separately for a normal CLI session and actual Desktop.
Hooks plus passive history reads remain a useful degraded capability; they do
not meet the requested live acceptance criteria.

## Interruption results

Each test used a read-only synthetic MCP echo delayed by twenty seconds.
The driver waited for item/started, allowed two seconds for hook/server entry,
then sent turn/interrupt to the exact disposable thread/turn IDs.

| Case | 0.153.4 and 0.160.1 |
| --- | --- |
| Root turn interrupted during tool | Explicit turn/completed status=interrupted; Interrupt hook; no MCP item/completed, PostToolUse or Stop before bounded process shutdown |
| Child turn interrupted during tool | Explicit child turn/completed status=interrupted; no SubagentStop and no Interrupt hook in the captured child/parent hook streams |
| Late child tool result | MCP item/completed status=completed arrives about eighteen seconds **after** interrupted child turn; matching PostToolUse exists |
| Parent after child interruption | Parent eventually finishes its own turn as completed; this does not make the child successful |
| Hook run cancellation | Every observed hook/started has a corresponding count of hook/completed in these samples; interruption specifically inside a running hook was not exercised |

The child MCP hook explicitly supplies agent_id = child thread ID and turn_id
= child turn ID, while session_id remains the parent thread. This is additional
hard correlation evidence. A root-only rule equating all hook session_id values
with App Server item threadId would be wrong.

Turn status, tool status and source completeness are independent. An interrupted
turn can contain a completed tool. A missing tool terminal stays TerminalUnknown;
the enclosing turn interruption is recorded as context, not fabricated tool
cancellation. Missing SubagentStop is reproduced on this path. No timeout is
translated into success.

## Code Mode, parallel calls and configuration reload

Both versions were restarted with only the isolated `features.code_mode=true`
override. The persisted transcript shows custom_tool_call named exec and
Promise.all/Promise.allSettled invoking the two synthetic MCP calls. A selected
tool-call-only transcript excerpt is committed; reasoning and account records
are excluded. This is RuntimeTranscriptVisible evidence, not proof of the exact
full model-consumed result.

Two distinct MCP items start before either completes. Each has its own exact
PreToolUse/PostToolUse pair with matching call IDs. Code Mode's outer exec/batch
has no corresponding tool hook or App Server batch item in these fixtures.
The synthetic server processes its requests serially, so overlapping runtime
items prove concurrently outstanding calls, not concurrent server execution.
No PostToolBatch equivalent was observed or found among the twelve hook names.

In the same live App Server process, changing the SessionStart command by one
trailing space changed its hash and hooks/list trustStatus from trusted to
modified. Restoring exact original bytes restored trusted. This proves inventory
reload and trust recalculation; it does not prove that every already running
turn adopts configuration changes. The probe restored its own file in finally.

## Measurements and limits

Repeatable measurements are in `research/codex/fixtures/measurements.json`,
computed by Measure-Probe.ps1 from raw streams and receipt sidecars.

| Sample | Messages | Raw bytes | Messages/s | Peak messages in 1s bucket | Largest notification bytes | Largest whole frame bytes |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 0.153.4 MCP comparison | 121 | 811063 | 4.25 | 43 | 957 | 756853 |
| 0.160.1 MCP comparison | 121 | 819250 | 3.73 | 48 | 1254 | 764234 |
| 0.153.4 three children/follow-up | 285 | 878319 | 4.59 | 39 | 957 | 756853 |
| 0.160.1 three children/follow-up | 264 | 874514 | 4.78 | 33 | 1254 | 757128 |

MCP comparison raw throughput was 28.5/25.3 kB/s including inventory. Largest
MCP result was 314 JSON-reencoded bytes, not the 757–764 kB tool catalog. This
tests small payload fidelity only; large-result truncation is unproven. Successful
MCP calls have four correlated evidence records, failed calls three. Repeated
arguments/results account for intentional duplicate representation; original
records must not be deleted as a deduplication optimization.

Hook capture-body median was roughly 53–57 ms in those four samples; maximum
79 ms. App Server's complete synchronous hook-run median was 580–605 ms,
maximum 741 ms, including process/startup overhead absent from the capture-body
timer. The PowerShell probe is therefore **not** a production latency target.
Keep the existing native Collector path; benchmark its full invocation before
setting production budgets. Collector queue depth, disk-full recovery, sustained
load, slow UI and large output were not measured. This is not a capacity claim.

Verification: 18 research tests pass, covering transport faults, actual IDs,
MCP error fidelity, child reuse, recovery versus subscription, missing terminal,
late child completion, Code Mode parallel identity and live trust inventory.
The capture and synthetic MCP byte-fidelity/fail-open self-tests pass. Main
solution restore/build/format/test results are recorded in the final report.
One intermediate research build failed because the experiment still held its
Windows apphost executable open; after the probes exited, build passed with
zero warnings/errors. No runtime or production setting was changed to fix it.
