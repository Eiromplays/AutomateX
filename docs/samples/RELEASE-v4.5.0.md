# AutomateX v4.5.0

AutomateX becomes an MCP server: call your workflows as tools from Claude and other MCP clients.

## Highlights

- **Workflows as MCP tools.** Flag a workflow "Expose as MCP tool" (name + description) and it appears
  to any connected MCP client. The model calls it with a `payload`, AutomateX runs the workflow, and
  the final step's output comes back — a real tool backed by real automation.
- **Run and wait.** `tools/call` runs the workflow and waits (bounded, `Engine__McpCallTimeout` ~60s)
  for it to settle, returning the output; a long workflow returns its execution id so it's never lost.
- **Safe by construction.** The server (`POST /api/mcp`, JSON-RPC) is off unless
  `Engine__AllowMcpServer=true` **and** an API key is configured — it refuses in open mode. Connections
  are workspace-scoped, only opted-in workflows are reachable, and every call is tagged `mcp:<tool>`
  and audited.

## Upgrade notes

- **Migration:** adds `McpExposed` / `McpToolName` / `McpDescription` to the workflow. Additive; nothing
  is exposed by default.
- **Config:** `Engine__AllowMcpServer` (default off) and `Engine__McpCallTimeout` (default 60s).
- **Connecting:** point a client at `POST /api/mcp` with `X-Api-Key` + `X-Workspace-Id` headers (the
  workflow's Connect panel shows the snippet).

See the [MCP server recipe](docs/recipes/mcp-server.md) and
[design note](docs/mcp-server-design.md). Full history: [CHANGELOG.md](CHANGELOG.md).

---

*Follow-ups tracked: finer per-workspace/per-workflow API keys (the current shared key is coarse), a
SignalR live-log stream for plugin ops, and an app-wide icon pass. All since closed — scoped API keys in
v4.6.0, the live-log stream and the icon pass in v4.9.0.*
