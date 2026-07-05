# AutomateX as an MCP server (v4.5)

AutomateX already speaks MCP as a *client* (`mcp.call`, `llm.agent`). This flips it: an MCP client —
Claude Desktop, an IDE agent, another AutomateX — connects to AutomateX and calls your workflows as
**tools**. "Summarise this ticket", "notify on-call", "enrich this lead" become tools the model can
discover and invoke, with the workflow doing the real work behind them.

## What's exposed

**Opt-in per-workflow tools.** A workflow gains an `McpExposed` flag plus an optional tool name and
description. Each exposed workflow becomes one MCP tool:

- **name** — an explicit `McpToolName` (a slug like `summarize_ticket`), defaulting to a slug of the
  workflow name. Unique per workspace.
- **description** — `McpDescription` (falls back to the workflow description) — this is what the model
  reads to decide whether to call it, so it matters.
- **inputSchema** — v1 exposes a single free-form `payload` object (the trigger payload the workflow
  already resolves as `{{trigger.payload…}}`), described by the tool description. *Declared* per-tool
  input schemas (named args the model fills) are a natural follow-up; free-form keeps v1 simple.

Only flagged workflows are reachable; nothing is exposed by default.

## Transport & auth

MCP over **HTTP** (the streamable-HTTP transport) — AutomateX is already an HTTP service. A single
endpoint (`POST /mcp`) implements the JSON-RPC methods `initialize`, `tools/list`, `tools/call`.

**Gating (deliberate by design):** the server is off unless `Engine__AllowMcpServer=true` **and** the
instance authenticates the request — OIDC, or a dynamic API key. If the instance runs in open mode,
`/mcp` refuses — exposing workflows to a model should never be accidental. The client presents its key
(and an `X-Workspace-Id`), so an MCP connection is scoped to one workspace and sees only that
workspace's exposed workflows.

*Since v4.6:* keys are dynamically-issued, scoped, and revocable (see `auth-keys-design.md`). Hand a
client a **`Workflow`-scoped** key and its `tools/list`/`tools/call` are confined to that one workflow —
the least-privilege MCP credential the "Future" note below originally called for.

## tools/list

Returns the workspace's exposed workflows as tools:

```json
{ "tools": [
  { "name": "summarize_ticket",
    "description": "Summarise a support ticket and post it to Slack.",
    "inputSchema": { "type": "object", "properties": { "payload": { "type": "object" } } } }
]}
```

## tools/call — run and wait

`tools/call { name, arguments: { payload } }`:

1. Resolve the tool name → workflow (workspace-scoped). Unknown/again-disabled → tool error.
2. Enqueue a run with `payload` as the trigger payload (the normal `RunWorkflow` path).
3. **Wait for the execution to settle**, bounded by a configurable timeout
   (`Engine__Mcp__CallTimeout`, default ~60s): a poll on execution status (the engine is async, so this
   is a bounded status poll, not a new sync engine path).
4. On settle, return the workflow's **result** as the tool content — the output of the terminal step
   (highest order that ran); a failed execution returns an MCP tool error with the step error.
5. On timeout, return a "still running" result carrying the **execution id**, so a long workflow isn't
   lost — the caller can check it in AutomateX (or, later, via a `get_execution` tool).

Runs triggered this way are tagged (e.g. `TriggeredBy = "mcp:<tool>"`) so they're visible and audited
like any other execution.

## Result shape

A workflow can fan out to several terminal steps; "the result" is defined as the **last step by order
that completed** (the common linear case → the final step's output). Documented so authors design the
exposed workflow to end on the step whose output they want returned. Returning *all* step outputs is a
possible option flag later.

## UI

On a workflow's detail/edit page: an **"Expose as MCP tool"** toggle with a tool name + description.
A small **Connect** panel shows the endpoint URL and an example client config (how to point Claude
Desktop / an agent at `POST /mcp` with an API key). The Plugins/Connections pattern (copyable config)
is the model.

## Testing

- Pure: tool-name slugging + uniqueness; `tools/list` shape from a set of exposed workflows;
  `tools/call` arg → payload mapping; result extraction (terminal-step output; failure → tool error).
- Engine: run-and-wait returns a fast workflow's output; a workflow past the timeout returns the
  execution id + running status.
- Protocol: `initialize`/`tools/list`/`tools/call` JSON-RPC round-trips; auth required; workspace
  scoping (a key for workspace A can't call workspace B's tools).

## Risks

- **Arbitrary invocation.** An MCP client can run any exposed workflow with any payload — that's the
  feature, but it's remote execution. Mitigations: explicit opt-in per workflow, API-key auth,
  workspace scoping, audit tagging, and (existing) per-step guards. Document that you only expose
  workflows you're happy for a model to call freely.
- **Blocking calls.** Run-and-wait ties up a request for the workflow's duration; the timeout bounds it
  and long jobs degrade to an id.
- **Result ambiguity** on fan-out — hence the documented "last terminal step" rule.

## Slicing

- **v4.5a** — this design note.
- **v4.5b** — expose model: `McpExposed`/`McpToolName`/`McpDescription` on the workflow + migration; a
  bounded run-and-wait helper (poll execution to terminal). Tests-first on the pure bits.
- **v4.5c** — the MCP server endpoint (`initialize`/`tools/list`/`tools/call`) over HTTP + API-key auth
  + workspace scoping. Tests on the protocol + call.
- **v4.5d** — UI: the expose toggle + name/description + a Connect panel with client config.
- **v4.5e** — wrap-up docs: CHANGELOG, a "connect Claude to your workflows" recipe, RELEASE-v4.5.0.
