# Out-of-process plugin sandboxing (v4.0)

> **Status: shipped in v4.0.0.** Out-of-proc is the only mode; the in-proc loader
> (`PluginLoadContext`, shadow-copy) is removed. The "Today (in-proc)" section below is kept as the
> motivation that led here. Author-facing notes:
> [README → Writing a plugin](../README.md#writing-a-plugin) and
> [RELEASE-v4.0.0](samples/RELEASE-v4.0.0.md). Two boundary rules landed beyond the original plan:
> plugin types are constructed via their longest constructor with optional params defaulted (services
> come from `ActionContext`, not ctor injection), and plugin-registered engine event listeners are no
> longer supported (the protocol has no listener channel — use a trigger like `execution.onFailure`).

The real isolation boundary: each plugin runs in its **own process**, not just its own
`AssemblyLoadContext`. A plugin can't read the host's or another plugin's memory, can't crash the
host, and gets its own dependency closure (and, later, its own resource limits). This is the largest
single change in the v3/v4 arc and the reason v4 is a **major** — so it opens with a **spike**, not a
rewrite.

## Today (in-proc) and why it's not enough

Plugins load into the host process via `PluginLoadContext` (a collectible `AssemblyLoadContext`). That
gives dependency isolation but not *fault* or *memory* isolation: a plugin that segfaults a native dep,
leaks, or spins the CPU takes the host with it, and it shares the host's address space. It also forces
fragile unification rules — `PluginLoadContext` must hand `AutomateX.Plugin.Sdk` and
`Microsoft.Extensions.*` back to the host or the plugin's `IAction<,>`/`ILogger` become foreign types —
and `PluginReflection.LoadableTypes` has to tolerate partial type-load failures. **All of that is
deleted when this lands.**

## Architecture

The engine launches a small child host, **`AutomateX.PluginHost`**, per plugin. The child loads the
plugin in-proc *to itself* and exposes it over a line protocol; the engine talks to it through stdin/
stdout pipes (length-prefixed JSON messages — JSON-RPC-shaped, no extra transport deps). The in-proc
`PluginAssemblies` + reflection registries are replaced by a `PluginProcess` supervisor.

```
engine ──(stdio: JSON frames)── AutomateX.PluginHost ── plugin.dll (IAction/ITrigger/IConnectionType)
```

## Protocol

**Host → plugin**
- `describe` → the plugin's descriptors — actions `[type, displayName, description, configSchema,
  resultSchema]`, triggers `[type, …, configSchema]`, connection types `[type, fields, isOAuth,
  isTester]`. Replaces reflection discovery; a bad type surfaces as a describe error from the child,
  not a host-side partial-load.
- `action.execute` `{type, configJson, context:{executionId, workflowId, stepOrder, idempotencyKey}}`
  → `{resultJson}` | `{error}`. (Config/result are already strings at the engine seam via
  `SdkActionExecutor` — this maps cleanly.)
- `trigger.run` `{triggerId, type, configJson, context}` → starts the long-running listener; runs until
  `cancel`.
- `connection.buildOAuthConfig` `{type, values}` → `OAuthConfig`; `connection.test` `{type, values}` →
  `ConnectionTestResult`.
- `cancel` `{callId}`.

**Plugin → host** (the callbacks `ActionContext`/`TriggerContext` expose today)
- `log` `{level, message}`.
- `trigger.fire` `{triggerId, payloadJson}` — the `Fire` callback.
- `trigger.state.{get,set,setIfAbsent,remove}` — `ITriggerState`, backed by the host's durable
  `WorkflowState` store (must round-trip to the host; the DB lives there).

### HTTP

`ActionContext.Http`/`TriggerContext.Http` are host-provided today (SSRF guard + service discovery +
masking). Two options:
- **Plugin-local HttpClient** configured at startup with the same SSRF handler policy. Simpler; the
  host still masks secrets in the returned output (it already does). **Recommended for v1.**
- **Host-proxied HTTP** (requests stream back over the pipe). Strictest parity, but heavy (body
  streaming) — deferred.

## Lifecycle

- A `PluginProcessSupervisor` (sibling to today's `PluginTriggerHost`) keeps one warm process per
  plugin — **not** spawn-per-call, or per-action latency dies. Calls multiplex over the one pipe.
- **Crash isolation:** a process crash fails its in-flight action/trigger (the step fails, exactly as
  an action exception does today) and the host survives; the supervisor relaunches with backoff.
- **Hot-reload becomes restart:** `PluginWatcher` kills + relaunches the process — no
  `AssemblyLoadContext` unload dance, and native deps actually unload.
- **Resource limits:** OS-level by construction now; cgroup/job-object caps are a follow-up.

## SDK / author impact

The goal is to **keep the authoring API stable** — `IAction<,>`, `ITriggerListener<>`,
`IConnectionType`, and the `[Action]`/`[Trigger]`/`[ConnectionType]`/`[Multiline]` attributes stay as
they are; the child `PluginHost` hosts them in-proc and does the marshalling. The **break** is the
runtime/packaging: plugins are loaded by `PluginHost`, must be recompiled against the v4 SDK, and the
deploy convention may change (plugin ships next to a host shim, or the host is shared). Because this is
the breaking window, **any wanted SDK-surface change rides v4** — flag them now (none pressing).

## Rollout (de-risk the big one)

1. **Spike (first):** run *one* existing plugin (the sample echo/delay, or `ssh`) fully out-of-proc —
   launch, `describe`, execute one action, verify result + a `log` callback + an HTTP call + a clean
   cancel. Measure warm-call latency and startup cost. This validates the protocol before committing.
2. **Preview behind a flag** (`Engine__OutOfProcPlugins`): out-of-proc opt-in for one release, in-proc
   still default, so it can bake against real plugins.
3. **Default + delete:** flip the default, migrate first-party plugins, and **delete** `PluginLoadContext`,
   the `PluginReflection.LoadableTypes` tolerance, and the in-proc executors.

## Risks

- **Biggest surface in the arc** — sequence it: spike → protocol+host → supervisor/lifecycle → migrate
  first-party plugins → delete in-proc → wrap-up, each its own commit.
- **Latency** — keep processes warm + multiplex; never spawn per call.
- **The trigger channel** — long-lived bidirectional (fire + state callbacks); over-test cancel,
  restart, and backpressure.
- **HTTP/SSRF parity** — plugin-local client must carry the guard policy; documented.
- **Cross-platform** — process + pipe semantics differ on Windows/Linux; CI both.

---

**Decisions to confirm:** (1) process-per-plugin + length-prefixed JSON over stdio; (2) plugin-local
HTTP with the SSRF policy (host-proxy deferred); (3) `trigger.fire` + `ITriggerState` round-trip to the
host; (4) keep the authoring API stable — the break is runtime/packaging + recompile; (5) spike →
flag-gated preview → default+delete, rather than a big-bang switch.
