# Isolated stdio App Server results

On 2026-10-13 the standalone .NET probe successfully initialized both exact
binaries, created one disposable thread each, submitted a model-only turn,
observed explicit turn completion and read the resulting history. Model was
gpt-6-astra for both; sandbox was explicitly read-only. No existing session
was resumed and no approval request was answered.

| Observation | 0.153.4 | 0.160.1 |
| --- | --- | --- |
| initialize userAgent | Codex Desktop/0.153.4, aigenterra_spike/1 | Codex Desktop/0.160.1, aigenterra_spike/1 |
| Platform | windows | windows |
| Turn outcome | completed | completed |
| Messages through final history/loaded reads | 36 | 37 |
| thread/started; turn/started; turn/completed | 1 each | 1 each |
| thread/status/changed | 2 | 2 |
| item/started; item/completed | 2 each | 2 each |
| hook/started; hook/completed | 3 each | 3 each |
| thread/tokenUsage/updated | 1 | 1 |
| MCP startup status | codex_apps starting/ready | codex_apps starting/ready |
| Hook payloads attached by exact session_id | 4 | 4 |

The initialized process identifies itself as **Codex Desktop** even when using
the npm CLI binary. This user-agent prefix is not proof of an actual Desktop
client or shared service connection. Runtime binary version and client surface
must be recorded separately. codex_apps is runtime-provided in these isolated
sessions; we did not copy user MCP configuration.

## Verified contract and discrepancies

- Hook session_id equals the created thread ID; UserPromptSubmit.turn_id equals
  both turn/started and turn/completed IDs. Golden tests lock down this actual
  cross-source relationship for both versions.
- Notifications include an observed top-level emittedAtMs. Treat this as source
  emission time with its own semantics, not proof of tool start time or global
  causal order. Local receipt time remains separate.
- The initial sandbox value readOnly was rejected by both binaries with -32600.
  Installed schemas require read-only/workspace-write/danger-full-access. The
  corrected request uses read-only. The official page's camelCase example is
  not the installed request contract.
- Both runs return a deprecationNotice for thread/read with includeTurns=true:
  full hydration is deprecated for paginated history; page with thread/turns/list
  and thread/items/list. The older capture script therefore needs a compatibility
  seam, even though the current bounded read still succeeds.
- The generated schema's presence of a hook or item does not establish all
  execution-path coverage. These smoke runs exercise no MCP invocation, file
  change, command or child agent.

## Evidence and verification

Original NDJSON streams and stderr remain in each temporary workspace. The
committed stdio-smoke fixtures select 20 protocol messages and four hook payloads
per version. Paths and UUIDs are jointly pseudonymized across both streams;
original-byte hashes and sizes remain in manifests. Account notifications,
reasoning items and message deltas are omitted from committed fixtures.

The client archives stream bytes before parsing, routes responses by request ID,
keeps server requests distinct from responses and never auto-approves them.
Its four transport tests cover fragmented multibyte/CRLF input, malformed JSON
archiving, reordered/duplicate responses and disconnect failure. Two additional
golden tests verify the measured thread/turn/hook identities and outcomes.

This is a spike outside the production solution. A successful privately owned
stdio process does **not** satisfy the passive observation acceptance gate.
