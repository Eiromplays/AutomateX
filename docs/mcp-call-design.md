# MCP client (`mcp.call`) — design proposal

**Status:** proposal for buy-in · **Date:** June 2026 · the v3.x "MCP client" arc.

## Goal

Let a workflow step invoke a **tool on an external MCP server** — turning the whole MCP
ecosystem into callable actions. (AutomateX-as-an-MCP-server is a separate, larger arc, deferred.)

## Transport

Modern MCP is JSON-RPC 2.0 over **Streamable HTTP** (a single endpoint; the client POSTs
messages, the server replies with either `application/json` or a `text/event-stream` SSE
stream). stdio/local-process transport is out — spawning processes server-side is a sandboxing
hazard. A bounded request/response call is:

1. `initialize` → server returns protocol version + capabilities (and maybe an `Mcp-Session-Id`
   header).
2. `notifications/initialized`.
3. `tools/call` with the tool name + arguments → a result with `content[]`.

We carry `Mcp-Session-Id` and `MCP-Protocol-Version` when the server uses them, and parse a
single SSE stream for the response message when the server answers that way. No server-initiated
streaming, sampling, or roots in Phase 1 — strictly client→server request/response.

## The decision: auth & connection model

### Option A — action only, templated headers (recommended, minimal)

`mcp.call` is just an action. Config: `serverUrl`, `headers` (templatable), `tool`, `arguments`
(templatable JSON). Auth is whatever header the server wants, e.g.
`Authorization: Bearer {{connections.myoauth.accessToken}}` — which **reuses existing
connections, including the OAuth we just built**, with no new connection type. Leanest, and it
composes cleanly with everything else.

### Option B — a dedicated `mcp` connection type

A stored `mcp` connection (server URL + token) that the action references by name. Nicer
"configure once" UX and a natural home for a future `tools/list` discovery dropdown in the
builder — but it's a second config surface and more code, and auth is less flexible than raw
templated headers.

## Tool selection (Phase 1)

Type the tool name + a JSON arguments object (templatable). A `tools/list` discovery dropdown in
the builder is deferred (needs a backend "list this server's tools" endpoint).

## Result mapping

`tools/call` returns `content[]` (text/resource blocks), optional `structuredContent`, and an
`isError` flag. Step output = `{ content, structuredContent? }`. `isError: true` fails the step
with the text content (so a failed tool surfaces like any other step failure). JSON-RPC protocol
errors (bad method, transport) also fail the step.

## Pure core (tests-first)

- JSON-RPC request build + response parse (result vs error, id matching).
- SSE event parse → JSON-RPC messages (find the response for our id).
- `tools/call` result → step output mapping (content concat, isError handling).

These are pure and DB/HTTP-free — locked first, exactly like the switch router and OAuth flow.

## Phasing

1. **Phase 1** — `mcp.call` action (model A or B), `initialize`→`tools/call`, result mapping,
   tests on the protocol core.
2. **Later** — `tools/list` discovery UI; an OAuth-aware `mcp` connection; then the separate
   AutomateX-as-MCP-server arc.

## Decision (resolved)

**Model B — a dedicated `mcp` connection type.** An MCP server is exactly "a remote system with
credentials + a capability catalog," which is what a connection is; it gives configure-once reuse
and a home for discovery. The clincher: **MCP tool input schemas are JSON Schema, which the
builder already renders via `SchemaForm`** — so a "pick server → pick tool → fill a guided
arguments form" UX comes almost for free. The action still receives the resolved `serverUrl` +
auth header (templated from the connection), keeping the action dumb and the engine unchanged; the
builder ties connection + tool discovery together.

OAuth-backed `mcp` connections reuse the OAuth flow we just built and come in a later phase; P1
uses a static bearer token.
