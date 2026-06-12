# `llm.agent` — design proposal

**Status:** proposal for buy-in · **Date:** June 2026 · the v3.x "bounded agent" arc.

## Goal

A **bounded** LLM agent step: give it a goal and a set of tools, it loops (reason → call a tool
→ observe) until it's done or a bound is hit, and returns the result plus an **auditable
transcript** of every tool call.

## Where we are

`llm.prompt` is a single-shot OpenAI-compatible call living in the **Llm plugin**. `McpClient`
(tools/list + tools/call) lives in **core**. There's no tool-calling loop yet, and `ActionContext`
gives an action only `Http` + `Logger` — no way to re-enter the engine and invoke other actions.

## Tool source — the decision

### MCP servers (recommended)

The agent draws its tools from one or more configured **MCP server connections**: `tools/list`
→ expose them to the model as OpenAI function tools → run the model's chosen tool via
`McpClient.CallToolAsync` → feed the result back. This **composes with the MCP arc we just built**,
needs **no engine changes**, and is the most general option (the whole MCP ecosystem becomes agent
tools). Because it depends on `McpClient` (core), `llm.agent` ships as a **built-in** action;
`llm.prompt` stays a plugin.

### Alternatives (not chosen now)

- **HTTP / fixed tools** declared in config — self-contained but limited and bespoke.
- **AutomateX-action whitelist** — let the agent call other AutomateX actions. Most powerful in
  theory, but needs SDK/`ActionContext` changes for nested invocation and perturbs the
  one-action-per-step engine core. Deferred.

## Bounded + auditable

- `maxIterations` (default 8), optional `temperature` / `maxTokens`, optional `allowedTools` filter.
- Output = `{ output, finished, iterations, transcript: [{ tool, arguments, result, isError }] }`.
- Non-deterministic by nature, but **every tool call and result is logged into the step output**,
  so a run is fully auditable — the same spirit as the gate/switch being pure and inspectable.
- Engine retries re-run the whole agent (and re-bill tokens) — documented, like `llm.prompt`.

## Loop

1. `tools` = aggregate `tools/list` across the configured MCP servers (name-collision handling if
   multiple).
2. `messages` = `[system?, user(goal)]`.
3. Up to `maxIterations`: POST `/v1/chat/completions` with `messages` + `tools`. If the reply has
   `tool_calls` → run each via `McpClient`, append tool-result messages, continue. Otherwise → take
   the final content, `finished = true`, stop.
4. Cap hit → `finished = false`, return the partial answer + transcript.

## Pure core (tests-first)

- MCP `tools` → OpenAI `tools` schema (`{type:"function", function:{name, description, parameters}}`).
- Parse assistant `tool_calls` (id, name, arguments) from a completion.
- Tool result → `role:"tool"` message mapping.
- Loop termination given a scripted model (no tool_calls → stop; cap → stop).

These are pure (no HTTP/DB) and get locked first, like the switch router, OAuth flow and MCP core.

## Config (Phase 1)

`{ model, baseUrl, apiKey?, system?, goal, mcpServers: [{serverUrl, token?}], maxIterations?,
temperature?, maxTokens?, allowedTools? }` — `mcpServers` templated from `mcp` connections; a
builder editor (server multi-select, like the mcp.call picker) comes after the core works.

## Open question

Tool source: **MCP servers** (recommended), HTTP/fixed, or the AutomateX-action whitelist?
