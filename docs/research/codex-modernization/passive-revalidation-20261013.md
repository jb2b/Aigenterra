# Passive observation revalidation — research commit 6.5

**Recommendation: KEEP_GATE_BLOCKED. Production commit 7 has not begun.**
The six previous research commits, reports and compatibility fixtures remain intact.
This is an additional Windows experiment on 2026-10-13, not a reinterpretation of
the earlier independently owned stdio experiments.

| Surface | Primary classification | What this means in this experiment |
| --- | --- | --- |
| Normal CLI | **UNKNOWN** | Partial live delivery is proven on the same shared server. Complete observation-only coverage and lifecycle neutrality are not proven: existing root events are missing, and reconnect loses an existing child's event delivery. |
| Actual Desktop | **PASSIVE_HISTORY_ONLY** | A genuinely Desktop-owned test chat can be read by a separate server. No supported live route to its owning runtime was found or successfully connected. |

The CLI classification applies to the requested complete, passive observation
contract, including neutrality. It does **not** mean that no live events arrived.
Calling this a fully supported observer would hide substantial coverage and
subscription-lifetime limitations. Neither surface satisfies the commit-6 gate.

## 1. Versions and executable identity

| Component | Previously tested | Revalidated now |
| --- | --- | --- |
| Standalone CLI | 0.153.4 | **0.162.1** |
| Desktop embedded runtime | 0.160.1, manually owned stdio probe | **0.160.1**, also identified as the actual running Desktop child process |
| Actual Desktop application | Not inferred or tested | **26.930.61225**, verified in Help → About ChatGPT; update diagnostic reports build **13232**, channel **prod** |
| Windows installed package | Not a runtime-version source | `OpenAI.Codex_26.930.7945.0_x64__2p2nqsd0c76g0`; distinct package identity, not the About version |
| Shared CLI daemon | Earlier versions did not establish a usable shared Windows service | **0.162.1** in the disposable short-path home; `daemon version` reports `running`, backend `pid`, matching CLI/App Server versions |
| User-home/Desktop daemon diagnostic | Earlier diagnostic failed | Still fails at the user-home control socket, Windows **10050**; not evidence of universal daemon absence |

The app reports **restart_required**. No app update or restart was performed.
The About screen and runtime executable version are independent observations.
The Electron/Chromium executable version is not used as the product version.

Paths below use `%USERPROFILE%` only to remove the local user's name:

- CLI: `%APPDATA%\npm\node_modules\@openai\codex\node_modules\@openai\codex-win32-x64\vendor\x86_64-pc-windows-msvc\bin\codex.exe`.
- Running Desktop runtime: `%LOCALAPPDATA%\OpenAI\Codex\bin\5ea220ae823df3d7\codex.exe`.
- Actual Desktop executable: `%ProgramFiles%\WindowsApps\OpenAI.Codex_26.930.7945.0_x64__2p2nqsd0c76g0\app\ChatGPT.exe` (package location recorded separately in the local audit).
- Disposable managed daemon: `%TEMP%\a65-cli\home\packages\app-server-daemon\releases\0.162.1-x86_64-pc-windows-msvc\bin\codex.exe`.

CLI executable SHA-256: `DD13BDB162A184FA04039488B234A8258EF9B76F84E926C505D5742B19249A6A`.
Desktop runtime SHA-256: `3B8F6E33CAA75F232558A3CF76FF9B87BB5EF6DBCF4996372F24E55C78B1B916`.
The disposable managed daemon executable has the same SHA-256 as the CLI.
Version/help/features/app-server help/daemon help/daemon version/agents help/proxy
help were captured for both binaries. Both advertise `daemon_auto_start`, `hooks`
and `multi_agent` as stable/enabled; `multi_agent_v2` is stable/disabled. CLI
`tui_app_server` is removed/true. Code Mode is disabled in the disposable CLI test.

## 2. Fresh schemas and changes

Fresh ordinary and experimental bundles were generated from each exact binary in
an OS temporary directory, then preserved in the private archive. Hashes refer to
`codex_app_server_protocol.schemas.json`, using the same convention as the baseline.

| Runtime / bundle | SHA-256 |
| --- | --- |
| Old CLI 0.153.4 ordinary | `E8284C5CB8157554A3DD1E035AADBD4325AEA501AF56887E9C2E12EB1B9B9448` |
| Old CLI 0.153.4 experimental | `B06F77062369D481A59CC70720C12B89CB9DD49C385863923262102D3AD6C978` |
| Desktop 0.160.1 ordinary, old and freshly generated | `8683F6FD9FBB994FA2BFB704AB5B02C9890238FA507A4C0FF33FF45884CD0A2E` |
| Desktop 0.160.1 experimental, old and freshly generated | `7243BA241962AF92CA60581F1A81808EBDA4212A800F8B205F54703BCFD508C5` |
| CLI 0.162.1 ordinary | `71A94EAB0D0B27F2AAB30993097BC13D78314EB386801C105280B53F0963AF44` |
| CLI 0.162.1 experimental | `E4E7F0C7D3FD77C48CD8619CAD2BF2B315D860AB006AD5A7EBE44F6E32E666C1` |

Compared with 0.160.1 experimental, 0.162.1 adds requests
`account/bedrock/checkGovCloudRequirements`, `thread/attachmentOwner/list` and
`thread/prediction/request`, plus notification `thread/prediction/updated`.
No request, notification or server-request method was removed in this comparison.
Attachment-owner lookup concerns stored artifacts, not an observer subscription.

The `InitializeCapabilities`, `ThreadReadParams`, `ThreadLoadedListParams`,
`ThreadTurnsListParams`, `ThreadItemsListParams` and `ThreadResumeParams`
definitions are unchanged. No distinct subscribe/observe RPC or read-only-client
capability appeared. The complete resume schema file changes because referenced
message definitions changed; its own parameter contract did not.

`Turn.rootTurnId` is new. `subAgentActivity` adds optional `model` and
`reasoningEffort` at child creation. These are useful explicit correlation and
provenance fields, not proof of delivery or a license to infer missing parents.
Thread identity/status, MCP result, collab agent state, twelve hook names and
hook-started/completed contracts remain present; the inspected definitions are
unchanged. Old originator/MCP metadata findings remain in the original baseline.

## 3. Intended topology versus actual connections

Current CLI help supports **both** shared and independently owned modes:
default daemon auto-start, `--no-daemon`, and `--remote` using ws/wss/unix.
`app-server` supports stdio, Unix sockets, WebSockets and `off`; `agents` browses
the shared daemon. This host successfully exercised the shared mode on Windows.

```text
normal CLI TUI ───────────────┐
                            ├── same managed App Server 0.162.1 ── CLI root + children
independent Unix WS observer ┘

actual Desktop UI ── owned stdio App Server 0.160.1 ── Desktop root + child
                                                       │ persisted history
independent stdio history reader 0.160.1 ────────────────┘
```

The CLI's daemon version diagnostic, managed process path, PID lifetime, loaded
thread list and events identify one shared server. The CLI owner PID and daemon
PID remained unchanged through observer disconnect/reconnect. Desktop's runtime
was a child of the running GUI process with `app-server` and no `--listen` or
`proxy` argument. Its PID remained unchanged. The Desktop history reader was a
different process with an empty loaded-thread list throughout.

Sharing a state directory is not sharing a server or runtime. Desktop's separate
reader never becomes a successful live attachment merely by reading fresh history.

The initial CLI home under the long archive path exceeded Unix socket `SUN_LEN`;
no task started there. Retrying owner setup under `%TEMP%\a65-cli` fixed that
environment issue. This was not an observer-driven daemon restart. Another initial
attempt treated `app-server proxy` as newline JSON-RPC. It timed out because proxy
forwards raw socket bytes: the server logged an invalid WebSocket upgrade. The
corrected observer uses WebSocket framing directly over the existing Unix socket.
Neither setup failure is counted as absence of live protocol support.

The [official App Server documentation](https://learn.chatgpt.com/docs/app-server)
describes history reads separately from start/resume subscriptions. An additional
source audit pinned OpenAI's repository at
[`979771476a6dc8cae59dfc9224b91ae0515b04fd`](https://github.com/openai/codex/tree/979771476a6dc8cae59dfc9224b91ae0515b04fd).
Its [Unix transport](https://github.com/openai/codex/blob/979771476a6dc8cae59dfc9224b91ae0515b04fd/codex-rs/app-server-transport/src/transport/unix_socket.rs)
accepts WebSockets. Its [thread processor](https://github.com/openai/codex/blob/979771476a6dc8cae59dfc9224b91ae0515b04fd/codex-rs/app-server/src/request_processors/thread_processor.rs)
and [lifecycle listener](https://github.com/openai/codex/blob/979771476a6dc8cae59dfc9224b91ae0515b04fd/codex-rs/app-server/src/request_processors/thread_lifecycle.rs)
contain connection subscriptions and child listener attachment. This source snapshot
helps explain observed asymmetry; it is not asserted to be the exact release source.

## 4. CLI experiment and reconnect

The owner was a **normal interactive CLI**, not `codex exec` or the Aigenterra
AppServerProbe. A new Git workspace and isolated home were prepared before owner
startup with one synthetic read-only MCP server, on-request approvals and a
read-only sandbox. Only synthetic prompts were submitted through the TUI. The
observer never starts work. No hook configuration was installed for this new CLI
case. The CLI model was `gpt-6-astra`, low reasoning.

| Scenario | Owner evidence | Independent observer evidence |
| --- | --- | --- |
| Root baseline | `CLI_BASELINE_READY` | Completed before attachment; terminal history readable |
| Root MCP success + deliberate error | Synthetic echo `CLI_MCP_OK`, fail `CLI_MCP_FAIL`; repeated with observer absent | Root active/idle broadcasts; **zero root turn/item/MCP lifecycle events** |
| Child creation/reuse | One child produces `CHILD_FIRST`, then `CHILD_SECOND`, then performs echo/fail | Three child turn-start/completed pairs; six item-start/completed pairs, including two MCP items |
| Parallel work | Parent echo `CLI_PARALLEL` while child handles follow-up | Child events arrive; parent MCP remains history-only |
| Approval | Same explicit harmless `Write-Output CLI_APPROVAL_PROBE` request prompts in owner, once attached and once disconnected; owner selects single-use approval | Zero server requests; no approval responses sent |
| Owner interrupt | CLI Escape interrupts `CLI_INTERRUPT_RETRY` during delayed MCP; history says interrupted | Root status returns idle; no root turn/completed event, so status alone cannot classify interruption |
| Active disconnect | Last snapshot contains a root turn `inProgress`; delayed echo subsequently returns `CLI_AFTER_DISCONNECT` | Observer closes only its socket; owner and daemon remain alive |
| Gap | Root MCP success/error and another approval execute while observer absent | Later history supplies terminal items; unseen intermediate events remain missing |
| Reconnect | Same root and old child remain usable; old child returns `CHILD_AFTER_RECONNECT`; another new child returns `NEW_CHILD_AFTER_RECONNECT` | Root/old-child status broadcasts resume. **No old-child turn/item events**. New child gets one turn-start/completed and one item-start/completed pair. |

The first attempted interrupt arrived after a successful turn had completed; it is
retained as an unsuccessful timing attempt, not labeled interrupted. The second
attempt was verified by the owning CLI and persisted interrupted state.

Connection 1 captured 103 messages (96 notifications); connection 2 captured 30
(23 notifications). History grew from 1 to 8 root turns during connection 1,
then from 10 to 11 during connection 2. A child-specific read verifies successful
reuse even when its post-reconnect turn/item events are absent. Snapshot pages
have null continuation cursors for these bounded samples; history completeness
does not imply event-stream completeness.

Both explicit observer close attempts recorded a WebSocket exception because the
peer closed without completing the close handshake. Owner continuity still passed.
This transport limitation remains visible in fixtures; it was not converted to a
successful graceful close. Reconnect itself requires only initialize/initialized
and read calls. It neither repairs subscriptions for old children nor replays
missed deltas. **SourceGap remains required.**

## 5. Actual Desktop experiment

The test chat was created with **New chat → Don't work in a project** in the
actual GUI. Its title is `Confirm desktop baseline readiness`; its workspace was
created by Desktop under Documents/Codex. The root baseline, MCP requests, child
creation/reuse, interruption and subsequent baseline were submitted through that
GUI. A manually launched bundled executable served only as a separately labeled
history reader. It did not create the observed chat.

- Baseline response: `DESKTOP_BASELINE_READY`, before and after reader disconnect.
- Existing Context7 MCP: `.NET` lookup completed; the deliberate
  `/aigenterra-probe/does-not-exist` documentation request failed with `isError:true`.
  That flag is present in the owner's original transcript; App Server history
  projects `status:failed`, `error:null` and result content without `isError`.
  No retry or configuration change was needed.
- Same child produced `DESKTOP_CHILD_FIRST` and `DESKTOP_CHILD_SECOND`.
  History has started/interacted/completed `subAgentActivity` entries with the
  same explicit `agentThreadId`; its own history has two completed turns.
- The owning GUI Stop button interrupted the disposable wait turn. History records
  `interrupted`. This proves owner interruption, not cancellation of an already
  executing sleep: the UI reported stopping immediately and tool-start timing is
  not established.
- The default user-home Unix control socket fails with Windows socket error 10050.
  The GUI's own runtime has no advertised shared endpoint in the inspected command
  line. No supported alternative passive connection to that process was established.
- Separate reader 1 saw one initial and four final turns, **zero thread/turn/item
  events**, and no loaded threads. Reader 2 saw five turns after the next GUI
  baseline, again without loading the thread. Account/connection notifications
  from a separate server are not live events from the Desktop-owned task.
- The GUI/runtime remained usable and the runtime PID was unchanged. Approval
  routing under a successful live Desktop attachment remains **not tested**.
  No approval was triggered by the read-only MCP sample; absence of a prompt does
  not establish routing neutrality.

An accidental GUI voice-control activation failed because no device was available;
the UI returned to the test chat. It was a UI automation timing error, not an
observer effect, and is excluded from runtime acceptance results.

## 6. Side effects, MCP semantics and limitations

Observers sent only `initialize`, `initialized`, `thread/loaded/list`, `thread/read`
and `thread/turns/list`. They never resumed, started, steered or interrupted a
thread, answered an approval, changed trust/configuration, or injected context.
The sent-message fixtures verify that boundary. The sole initialize capability
was `experimentalApi:true`; no gateway/OAuth/runtime-setting capability was set.

The CLI root's `source:"vscode"` and `originator:"Codex Desktop"` still appeared
despite a verified normal TUI owner, in this Desktop-launched test environment.
These fields are not reliable proof of the owning frontend. Process topology,
owner launch and GUI evidence take precedence. This environment dependence needs
explicit modeling in a future capability profile.

Synthetic MCP raw responses preserve `isError`; observed child App Server items
use completed/failed status and retain result content/structuredContent/_meta.
The failed item has `error:null`. Root successful/error calls are present in
history but absent from root live delivery. Two repeated root MCP pairs, with
and without observer, have the same arguments and synthetic result payloads.
This is bounded tool behavior evidence, not proof that every model/tool outcome
is unaffected by attachment.

Owner usability and continuation were verified. Root approval prompting remained
with the CLI in both cases. No observer operation writes model input or tool data.
However, semantic identity of all model inputs/outputs is **not proven** by a
small nondeterministic sample. Neither are child approval routing, lifecycle
neutrality, unload timing, production load or all thread ownership invariants.
New-child event delivery implies server-managed subscription behavior; whether
the extra connection prolongs child lifetime is unknown. Do not equate a read-only
outgoing RPC list with proof of zero runtime side effects.

Hooks were not reconfigured. No hook lifecycle was observed by these new readers;
this does not invalidate prior hook fixtures or prove hooks are absent. Actual
Desktop capture hooks may remain active under existing user configuration.
No daemon restart, model instruction rewrite, tool rewrite, injected proxy,
Desktop runtime replacement or writer-lock workaround was used to gain delivery.
Cleanup stopped only the experiment-created CLI daemon after owner exit and the
disposable test database; all temporary auth copies were removed. Desktop and
the user's production database service remain running.

## 7. Evidence and verification

New committed fixtures (existing directories are preserved):

- [`0.162.1/passive-revalidation-20261013`](../../../research/codex/fixtures/0.162.1/passive-revalidation-20261013): two same-server connections, child history, sent RPCs, selected events, snapshots and external synthetic MCP responses.
- [`0.160.1/actual-desktop-revalidation-20261013`](../../../research/codex/fixtures/0.160.1/actual-desktop-revalidation-20261013): actual-GUI root/child history and reader notification counts.
- Old `0.153.4` and `0.160.1` fixture cases remain byte-for-byte unchanged.

Each new manifest records runtime executable SHA-256 and ordinary/experimental
schema hashes. Each connection records its original transcript hash. IDs are
jointly pseudonymized across live/history/tool evidence; account fields, paths,
reasoning and model prose are omitted. The private archive is
`%USERPROFILE%\.codex\artifacts\aigenterra-passive-revalidation-20261013`.
It contains generated schemas, help/version/source audits, exact stdio bytes,
decoded WebSocket payload bytes before JSON parsing, received/sent transcripts,
receipt timestamps, original owner histories, MCP responses, UI screenshots and
verification logs. WebSocket payloads are application bytes, not TCP/TLS frames;
`received.ndjson` adds framing. CLI terminal text has ANSI controls removed and
is supplemental evidence. The archive index hashes local evidence files.
The first owner's bootstrap package cache is also retained locally, outside Git;
it is not runtime event evidence.

Representative **selected fields** from the sanitized CLI child payload:

```json
{
  "method": "item/completed",
  "params": {
    "threadId": "00000000-0000-4000-8000-000000000002",
    "turnId": "00000000-0000-4000-8000-000000000006",
    "item": {
      "type": "mcpToolCall",
      "id": "exec-00000000-0000-4000-8000-000000000009",
      "server": "observability_probe",
      "tool": "fail",
      "status": "failed",
      "error": null,
      "arguments": { "marker": "CHILD_MCP_FAIL" }
    }
  }
}
```

The root instead emits a status payload such as
`{"threadId":"00000000-0000-4000-8000-000000000001","status":{"type":"idle"}}`.
This supplies neither turn identity nor outcome. The Desktop failed MCP item is a
`thread/turns/list` response item, not an `item/completed` notification. Its
`tool:"query-docs"`, `status:"failed"` and `error:null` must retain that provenance.

The isolated [`PassiveObserver`](../../../research/codex/PassiveObserver/README.md)
is not referenced by any production project. Fixture regression tests distinguish
live root status from missing root events, verify child MCP error semantics,
preserve reconnect gaps, verify Desktop history without subscriptions and reject
forbidden outgoing observer methods in the captured experiment.

Validation: research restore/build and **23 tests** pass (18 existing + 5 new);
format verification and PowerShell syntax checks pass. Main solution restore,
build, format verification and **163 tests** pass against the disposable
PostgreSQL cluster; hook capture smoke checks also pass. No production project,
migration, API, Domain or Application contract changed. No tests were weakened.
The fixture exporter was run again into a fresh temporary destination; its
overwrite refusal and resulting fixture shape passed smoke checks.

## 8. Gate and independent value of commits 7–9

**KEEP_GATE_BLOCKED** is the single recommendation. The original requirement
remains unmet. The current Desktop build has no proven live route, and CLI
partial delivery is not a complete, reconnectable, neutral observer contract.
Do not replace this gate with a weaker one implicitly.

| Proposed later commit | Independently useful? | Boundary if separately authorized |
| --- | --- | --- |
| 7 — versioned runtime evidence | Yes | Immutable raw capture envelopes, provenance and retention can serve existing hooks/history without claiming live completeness. Preserve old evidence and fail-open capture. |
| 8 — versioned normalization/projections | Yes | Deterministic replay of existing/versioned fixtures, explicit IDs and Unknown states can improve interpretation. Never turn missing terminal evidence into success or inferred topology. |
| 9 — runtime source health/capabilities | Yes | Surface source-specific availability, partial coverage, version mismatch and SourceGap. Keep CLI/Desktop and root/child capabilities separate. |

These are technically separable, but the approved commit-6 plan gates production
implementation before commit 7. Starting any of them still requires a separate
user decision explicitly changing that sequencing/scope and authorizing production
work. This report does not approve such a change. A future passive test must prove
root and pre-existing-child lifecycle/MCP delivery, safe reconnect, approval routing
and lifecycle neutrality on the actual supported Desktop build as well as CLI.

**Stop at research commit 6.5. Commit 7 has not begun.**
