# v4.9 — live plugin-log stream + app-wide icon pass (design)

Another **loose-ends release**: it clears the two follow-ups that have ridden along since v4.4/v4.5 —
a live plugin-log stream (complementing the polled console) and a consistent icon pass over the UI.
Unrelated subsystems, so they ride as **two independent slice tracks** with their own gates:

- **Track A — live plugin-log stream (backend + frontend).** Push plugin log lines to the ops console
  as they're appended, instead of only surfacing them via the `GET /plugins/{name}/logs?since=` poll.
  Reuses the existing SignalR hub pattern.
- **Track B — app-wide icon pass (frontend).** Replace the ad-hoc emoji/text glyphs (🔗 ⛓ ✕, status
  dots) with one tree-shakeable icon set, applied consistently across nav, action/trigger badges, and
  status chips.

Neither track touches the other's files; either can be built first. **No migration in the whole release.**

## Track A — live plugin-log stream

### What & why

A plugin host's output already lands in a bounded per-plugin `PluginLogRing` (`PluginLogLine(Seq, At,
Level, Source, Message)`, cap 500, monotonic `Seq`). The ops console (`plugin-ops.tsx`) reads it by
**polling** `GET /plugins/{name}/logs?since=<cursor>` on a timer — so a burst of plugin output shows up
a poll-interval late, and a live tail flickers. The engine already streams *execution* events over
SignalR (`ExecutionEventsHub` + `SignalRExecutionEventListener`, workspace groups); plugin logs should
get the same treatment so the console tails in real time. The poll endpoint stays as the snapshot +
backstop, so nothing regresses if the socket drops.

### Shape

- **`IPluginLogSink`** (new, `Engine.Plugins`) — `void Publish(string pluginDll, PluginLogLine line)`.
  Default `NullPluginLogSink` (no-op) registered in engine DI; the Web layer overrides it. Keyed by
  **dll path** (name-agnostic) — the engine never needs to know the plugin's public name.
- **`PluginProcessSupervisor` fans out on append.** Every appended line already flows through the
  supervisor's `IPluginHostCallbacks.OnLog` seam (each `PluginClient` calls it right after
  `Logs.Add(...)`). That seam widens to carry the plugin dll + the created `PluginLogLine` (so `Seq`
  ships), and forwards it to the injected `IPluginLogSink`. The ring behaviour is unchanged.
- **`PluginLogsHub`** (new, `AutomateX.Web`, mapped at `/hubs/plugin-logs`) — a **dedicated** hub, not a
  method on `ExecutionEventsHub`, because the auth boundary differs: `JoinPlugin(name)` validates
  **instance-admin** (mirroring the logs endpoint, which is instance-admin-only — logs can carry
  sensitive runtime data), whereas execution groups gate on workspace membership. Group key
  `plugin:{name}`.
- **`SignalRPluginLogSink`** (new, `AutomateX.Web`) — the `IPluginLogSink` impl: resolves dll → global
  plugin name (via `PluginAssemblies`, the same map `GetPluginStatus.ResolveDll` uses in reverse) and
  broadcasts `hubContext.Clients.Group("plugin:{name}").SendAsync("pluginLog", line)`. Best-effort, like
  the execution bridge.
- **Frontend: snapshot-then-stream.** `plugin-ops.tsx` keeps its initial poll (snapshot up to cursor
  `N`), then opens the hub (a `use-plugin-logs` hook mirroring `use-engine-events.ts`), `JoinPlugin`,
  and appends streamed lines with `Seq > N` — Seq makes the handoff gap-/dupe-proof. The poller stays as
  a reconnect backstop (the ring retains 500 lines, so a dropped line reappears on the next snapshot).

### Slice plan (Track A)

**m1 — `IPluginLogSink` seam + supervisor fan-out (backend, tests-first).**
Add `IPluginLogSink` + `NullPluginLogSink`; widen the supervisor's `OnLog` seam to carry the dll + the
`PluginLogLine`, and publish to the sink after the ring append. Engine registers the null sink. Unit
test: a fake sink, drive a line through the supervisor's log seam, assert it publishes `(dll, line)`
with the right `Seq` and that `LogsSince` still returns the same line (ring unaffected).
*Verify:* `dotnet test`; the poll path is byte-for-byte unchanged with the null sink.

**m2 — `PluginLogsHub` + `SignalRPluginLogSink` (backend, auth-sensitive).**
Add the hub (`JoinPlugin` gated on `IsInstanceAdmin`, group `plugin:{name}`), map `/hubs/plugin-logs`,
and the SignalR sink (dll → name, broadcast `pluginLog`). Register the sink over the null default. Test:
`JoinPlugin` declines a non-admin connection (no group add); an admin join receives a published line.
*Verify:* a non-admin can't join; a published line reaches an admin group member. High-stakes auth — use
a subagent for the review.

**m3 — Live tail in the ops console (frontend).**
`use-plugin-logs` hook + `plugin-ops.tsx` subscribe: snapshot via the existing poll, then stream by
`Seq`, poller as backstop. No new server surface.
*Verify:* `pnpm test/typecheck/lint/build`; manual check that a tail updates live and reconciles after a
reconnect without gaps or dupes.

## Track B — app-wide icon pass

### What & why

The UI leans on emoji and text glyphs for iconography — 🔗 (connection ref), ⛓ (step ref), ✕ (remove),
status dots, nav items. They render inconsistently across platforms/fonts and don't scale or theme. A
single tree-shakeable icon set gives consistent, accessible, themeable icons. There is **no icon
dependency today**, so this track introduces one and applies it in a bounded pass — no behaviour change,
purely presentational.

### Shape

- **Add `lucide-react`** (MIT, tree-shakeable, the conventional pairing with React + Tailwind + Radix in
  this stack). *This is the one dependency decision — see Calls made.*
- **A small icon map** for the domains that currently use glyphs: action/trigger **type** badges (keyed
  off `actionType`/trigger type), execution + plugin **status** chips (running / completed / failed /
  waiting / disabled), and the inline builder controls (reference inserter, remove-row, add-row).
- **Scope is the existing glyph sites**, not a redesign: nav sidebar, the plugins page status chips, the
  executions list/detail status, and `schema-form.tsx`'s inline controls. Sizes/colours come from
  Tailwind classes already in use; no layout change.

### Slice plan (Track B)

**m4 — Icon pass across nav, status chips, and builder glyphs (frontend).**
Add `lucide-react`; introduce the icon map; replace the emoji/text glyphs at the sites above. Keep
`aria-label`s where a glyph carried meaning (e.g. remove). Nothing server-side changes.
*Verify:* `pnpm test/typecheck/lint/build`; visual pass that each replaced glyph reads correctly and
keeps its accessible label.

## Calls made (both tracks)

- **Plugin logs are instance-admin, not workspace-scoped.** Unlike execution events (per-workspace
  group, member join), the plugin-log stream is an ops view over global plugins: a **dedicated
  `PluginLogsHub`** whose `JoinPlugin` requires instance-admin, mirroring the existing logs endpoint. No
  per-workspace fan-out; no widening of the execution hub's auth.
- **Stream is additive; the poll is the source of truth.** The client snapshots via the poll endpoint,
  then streams `Seq > cursor`; on disconnect it falls back to polling. Lines are best-effort (same as
  execution events) — the 500-line ring guarantees a dropped line reappears on the next snapshot, so the
  stream never has to be reliable.
- **The engine seam stays name-agnostic.** `IPluginLogSink` is keyed by dll path; the dll → public-name
  mapping (and the group naming) lives entirely in the Web-layer sink, so `Engine.Plugins` gains no
  dependency on Web concepts. Default is a no-op sink, so the engine and its tests don't need SignalR.
- **Not on the domain event bus.** Log lines are high-frequency, non-persisted output, so they use the
  dedicated sink rather than `EngineEventBus`/`IEngineEventListener` (which is for domain events
  published after state is persisted). Per-line send matches the ring's per-line `Add`; batching is a
  possible later optimisation, out of scope here.
- **Icon set = `lucide-react` (proposed, confirm before m4).** Tree-shakeable, MIT, matches the stack.
  If you'd rather use `@radix-ui/react-icons` (already in the Radix family) or another set, it only
  changes the import + the icon map, not the slice shape.
- **No migration, no new entity, no config key.** Track A is a runtime stream over existing data; Track
  B is presentational.
- **Commit scopes:** Track A `feat(plugins): … (v4.9 mN)` (m3, frontend, is `feat(web)`); Track B
  `feat(web): … (v4.9 mN)`; final wrap-up `docs(...): … (v4.9 mN)`.

## Release wrap-up

**m5 — Docs + `CHANGELOG` v4.9.0 (no code).**
`CHANGELOG` v4.9.0 entry (both tracks); recipe/ops note that the plugin Logs console now tails live
(instance-admin), extending the plugin-operations recipe; note the icon pass. Clear the two long-standing
v4.5.0 follow-up lines (SignalR live-log stream; app-wide icon pass). Final verify: `dotnet test` +
`pnpm` gate green, diff review.
