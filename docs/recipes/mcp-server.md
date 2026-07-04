# Recipe: connect Claude to your workflows (MCP server)

AutomateX can act as an **MCP server**: your workflows become tools an MCP client (Claude Desktop, an
IDE agent, another AutomateX) can call. "Summarise this ticket", "notify on-call", "enrich this lead" —
the model calls the tool, your workflow does the work, and the result comes back.

## 1. Turn the server on

Two things are required (by design — exposing workflows to a model should never be accidental):

- `Engine__AllowMcpServer=true`
- an API key set (`Auth__ApiKey`) — the server refuses if the instance runs open.

## 2. Expose a workflow

On a workflow's page, open the **MCP tool** panel and tick **Expose as MCP tool**. Give it:

- a **tool name** (or leave blank for a slug of the workflow name) — this is what the model calls;
- a **description** — this is what the model reads to decide *whether* to call it, so make it clear.

Design the workflow to end on the step whose output you want returned — the tool result is the final
step's output.

## 3. Connect a client

Point your MCP client at:

```
POST https://<your-instance>/api/mcp
  X-Api-Key:       <your API key>
  X-Workspace-Id:  <workspace id>
```

The connection is scoped to that workspace and sees only its exposed workflows. The Connect panel on
the workflow page shows this snippet.

## 4. Call it

The client runs `initialize` → `tools/list` (your exposed workflows) → `tools/call`. A call passes a
`payload` object (which becomes `{{trigger.payload…}}` in the workflow); AutomateX runs it, waits up to
`Engine__McpCallTimeout` (~60s) for it to finish, and returns the final step's output. A long workflow
returns its execution id instead, so nothing is lost — check it in AutomateX.

## Notes

- Every MCP-triggered run is tagged `mcp:<tool>` and shows up in executions + the audit log like any
  other run.
- Only expose workflows you're comfortable a model calling with arbitrary input — it's real execution.
- The single shared API key is coarse; finer, per-workspace keys are on the roadmap.
