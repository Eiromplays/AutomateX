# Plugin operations (v4.4)

v4.0 made every plugin its own child process behind `PluginProcessSupervisor`. That boundary is also an
**operations surface** we don't yet expose: you can't see a plugin's logs, whether its process is
healthy, what it's using, or restart/quarantine/limit it. This theme adds that, in three slices —
observability first (read-only, low risk), then lifecycle controls, then resource limits.

Everything hangs off the supervisor, which already owns one warm process per plugin dll.

## Observability (v4.4b)

- **Logs / console.** The supervisor captures each plugin's `OnLog` callbacks plus the child's
  stdout/stderr into a **bounded per-plugin ring buffer** (e.g. last 500 lines, monotonic cursor). Two
  ways out, as decided: poll `GET /plugins/{name}/logs?since=<cursor>` for history/tail, and a **live
  SignalR channel** (per-plugin subscription on the existing hub) for a real-time console. The buffer
  is the source of truth; the stream is a tee.
- **Status.** Per-plugin process state (`running` / `exited` / `never-started`), pid, started-at +
  uptime, restart count, and last-exit reason — surfaced in `GET /plugins` and `GET /plugins/{name}/status`.
- **Resource usage.** CPU% (sampled over a short interval) and memory (`WorkingSet64`) per child
  process, read on demand from `System.Diagnostics.Process` — cross-platform, no continuous polling.

## Lifecycle controls (v4.4c)

- **Restart.** `supervisor.Recycle(dll)` (per-plugin sibling of the existing `RecycleAll`): dispose the
  warm client so the next call relaunches. `POST /plugins/{name}/restart`.
- **Enable / disable.** A persisted per-plugin flag (a small `PluginState{Name, Disabled}` table). A
  disabled plugin is **not launched**, and the registries skip describing it — so its actions, triggers,
  and connection types disappear until re-enabled (workflows using them then fail clearly). This is the
  quarantine switch for a misbehaving plugin. `POST /plugins/{name}/enabled { enabled }`.

## Resource limits (v4.4d) — opt-in

All default **off** (null/0 = unlimited), so existing heavy plugins aren't broken by a surprise cap:

- **Per-call timeout.** Cancel an `action.execute` that runs longer than the budget (the cancellation
  token already flows to the host; enforce a linked timeout in the supervisor).
- **Memory cap.** A watchdog samples each child's `WorkingSet64`; over budget → kill + recycle, the
  in-flight step fails. Cross-platform baseline; Linux **cgroup**-backed hard caps are a later
  enhancement noted but not built here.
- **Max concurrency.** A per-plugin `SemaphoreSlim` bounds in-flight calls to one host process.
- **Config.** `EngineOptions.PluginLimits` — global defaults plus optional per-plugin overrides.

## API

```
GET   /plugins                       augmented: state, uptime, restarts, cpu, memoryBytes, disabled
GET   /plugins/{name}/status         the same for one plugin
GET   /plugins/{name}/logs?since=    ring-buffer tail (cursor-based)
POST  /plugins/{name}/restart
POST  /plugins/{name}/enabled        { enabled }
(SignalR) plugin-logs channel        live tail, subscribed per plugin name
```

## Roles

Status + usage are readable by workspace members (operational visibility). **Logs and all controls are
instance-admin only** — logs can contain sensitive runtime data, and restart/disable/limits are
instance-wide (plugins are global). Mirrors how plugin upload is already gated.

## UI

The Plugins page gains, per plugin: a **status badge** (running / disabled / crashed), CPU + memory,
restart count, and configured limits. Expanding a plugin opens a **live console** (SignalR tail +
scrollback from the buffer) with **Restart** and **Enable/Disable** buttons. Disable shows a warning
that the plugin's actions vanish from workflows until re-enabled.

## Testing

- Supervisor: log capture into the ring buffer + cursor paging; per-plugin `Recycle` relaunches;
  concurrency semaphore bounds in-flight calls.
- Disabled plugin: not launched, and its actions/triggers absent from the registries.
- Limits: the timeout and memory-budget *decision* logic in isolation (a watchdog kill is awkward to
  assert end-to-end; test the predicate + that a recycle is requested).

## Risks

- **CPU sampling cost** — sample on demand for the status call, never in a hot loop.
- **Killing mid-action** — a limit breach fails that step; that's the contract, surfaced as a normal
  step error.
- **Disable removes actions** — intended (quarantine), but it can break dependent workflows; the UI
  warns, and it's reversible.
- **Log buffer memory** — bounded per plugin; old lines drop.

## Slicing

- **v4.4a** — this design note.
- **v4.4b** — observability: supervisor ring buffer + status + usage; `GET /plugins` augmentation +
  `/logs` + `/status`; SignalR log channel; Plugins page status + live console.
- **v4.4c** — controls: per-plugin restart + persisted enable/disable (registries + supervisor honor
  it); API + UI buttons.
- **v4.4d** — limits: per-call timeout + memory watchdog + max concurrency (opt-in) + wrap-up docs
  (CHANGELOG, recipe, RELEASE-v4.4.0).
