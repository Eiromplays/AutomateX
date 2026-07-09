# v5.0 — multi-node / HA (design)

Retires the deliberate single-node design so the engine can run clustered: Wolverine leaves
`DurabilityMode.Solo` (`AutomateXEngine.cs:32`), the six background workers stop assuming they're the only
instance, and the SignalR fan-out stops being in-process. Multi-track, engine-first (tests-first); the
durability/execution and singleton slices are concurrency-critical and get a subagent review (like v4.9 m2,
v4.10 m1).

## What & why

AutomateX is single-node **by construction** today. Wolverine runs `Solo` (no leader election, all agents
local — `AutomateXEngine.cs:31-32`), each `AddHostedService` worker runs once per process and assumes it's
alone, and both hubs fan out over an in-proc `IHubContext` (`SignalRExecutionEventListener`,
`SignalRPluginLogSink`). That's the "one container" story (`deploy-homelab.md:5` — Postgres + API + web).

v5.0 removes that assumption so you can run **N API nodes** behind a load balancer for HA and horizontal
execution throughput, with **Postgres staying the single source of truth**. Three things change: (1)
Wolverine durability + where executions run, (2) singleton background work that must not double-run, (3)
cross-node fan-out. This is tractable because the engine is already *stateless compute over Postgres state*
— executions, steps, workflow state, and idempotency all persist; no execution state lives in a node's
memory. The change is designed to **degrade to today**: `Solo` + no Redis = the exact current single-node
behaviour, so it lands incrementally and existing single-node deploys are untouched.

## Shape

### Durability: Balanced + a shared Postgres queue for the execution lane

- **`Solo` → `Balanced`.** Balanced (the default) turns on Wolverine's leader election and distributed
  durability agents; *any* mode other than Balanced disables them. Balanced needs message persistence (have
  it — `PersistMessagesWithPostgresql`) plus a cross-node control channel; with no broker Wolverine falls
  back to a DB-backed control queue, so a **Postgres-only** cluster works — no Rabbit/ASB.
- **Mode becomes config, not a constant.** Read `Engine:DurabilityMode` (default `Balanced`) inside the
  shared `AddAutomateXEngine`. The test fixture and the Aspire AppHost set `Solo`, so single-node dev/test
  keeps agents auto-starting on the one node and stays fast — the shared composition root still guarantees
  no app/test drift.
- **Execution lane moves to a shared queue.** Durable *local* queues run work on the node that enqueued it
  (failover, not distribution). For competing-consumers execution, route `RunWorkflow` and the internal step
  messages (`ExecuteStep`/`AdvanceExecution`/`ResumeExecution`/…) to a **database-backed queue** via the
  Wolverine Postgresql transport (`ListenToPostgresqlQueue`, `AutoProvision`), which multiple nodes pull from
  competitively. The **outbox is unchanged**: `CronScheduler`/`PluginTriggerHost` still publish through
  `IDbContextOutbox` in the same transaction that commits `NextRunAt`; the envelope just lands on the shared
  queue where any node claims it once.
- **No app migration for this.** Wolverine self-provisions its own schema — node, assignment, and queue
  tables — none of it in EF (confirmed: no `wolverine` references in `src/AutomateX/Migrations`). The v4.10
  lesson still binds any slice that touches `AutomateXDbContext`; none of these do.

### Singletons: Wolverine agents, not N hosted services

The six workers each run once per node today. Under N nodes they split three ways:

- **`PluginTriggerHost` — the real hazard.** It starts one listener per enabled plugin-trigger row, either
  an in-host loop or an out-of-proc plugin process (`PluginTriggerHost.cs:120-145`). N nodes = N live
  listeners per trigger = **duplicate `RunWorkflow` fires per event**. Must be single-owner.
- **`CronScheduler` — already safe, still wasteful.** Its atomic `UPDATE … RETURNING` lease
  (`CronScheduler.cs:42-49`) means a concurrent node can't double-fire — worst case a fire is delayed by the
  5-minute lease. Correct under N nodes, but every node polls redundantly; single-owner drops the waste and
  the lease reasoning.
- **`StuckExecutionSweeper` / `RetentionSweeper` — idempotent, still noisy.** Set-based
  `ExecuteUpdate`/`ExecuteDelete`, so N nodes are correct but do the scan N times and emit duplicate
  "swept/pruned" log lines. Single-owner cleans that up.
- **`PluginMemoryWatchdog` / `PluginWatcher` — NOT singletons.** They police *this node's own* warm
  plugin-host processes (`PluginProcessSupervisor` is per-node). They must run on **every** node — making
  them singletons would leave peers' plugin hosts unpoliced. Leave them as `AddHostedService`.

**Model:** register `CronScheduler`, `PluginTriggerHost`, `StuckExecutionSweeper`, `RetentionSweeper` as
Wolverine **SingularAgents** (`SingularAgent` + `AddSingularAgent<T>`, present in WolverineFx 6.13). The
leader assigns each to exactly one live node and reassigns on node loss; in `Solo` they auto-start on the
single node, so tests are unchanged. Conversion is mechanical — each worker's `ExecuteAsync` `PeriodicTimer`
loop moves under the agent's `startAsync`/`stopAsync`. They stop being registered via `AddHostedService`
(registering both ways would double-run).

### Cross-node fan-out: SignalR Redis backplane + a plugin-reload broadcast

- **Backplane.** `IHubContext.Clients.Group(...).SendAsync(...)` only reaches connections on the local node,
  so a client on node B never sees an event produced on node A. **Redis is the only first-party self-hosted
  SignalR backplane** (`AddStackExchangeRedis` after `AddSignalR`); there is no maintained first-party
  Postgres backplane. So v5.0 adds **one Redis, purely for SignalR fan-out** — Postgres stays the
  durability/leader backbone. Once it's on, `SignalRExecutionEventListener` (workspace groups) and
  `SignalRPluginLogSink` (per-plugin groups) fan out across nodes with **no code change** — they already
  publish to `IHubContext` groups.
- **Backplane ≠ no sticky sessions.** SignalR's negotiate → WebSocket handshake must hit the same node.
  Either pin sticky sessions at Caddy or run WebSockets-only + `skipNegotiation`. Documented in the compose
  change; no client change (the web app already reconnects, which covers node failover).
- **Plugin log tail closes for free.** `SignalRPluginLogSink` only sees lines from hosts *on its own node*
  (per-node supervisor). With the backplane, each node's sink publishes into the shared plugin group, so an
  admin on any node receives every node's lines. No extra work — worth stating.
- **Plugin reload across nodes (the one genuinely new message).** A hot-reload or plugin upload today
  recycles only *local* hosts (`PluginReloader` → `supervisor.RecycleAll`) and re-describes; on N nodes the
  peers keep running stale plugin code. Add a `PluginsChanged` Wolverine broadcast: the reloading/uploading
  node publishes it, every node handles it by recycling its own supervisor and re-describing.

### Plugin hosts stay per-node (confirmed fine)

`PluginProcessSupervisor` owns warm child processes local to its node and routes calls to them
(`trigger.fire` → outbox enqueue, `trigger.state.*` → durable `WorkflowState`). Nothing assumes a single
cluster-wide instance. Per-node supervision is the right call: it keeps plugin blast-radius node-local and
needs no shared plugin runtime. The only shared concern is the plugin **files** (below).

### Config + deploy (this breaks the one-container assumption)

- **New keys.** `Engine:DurabilityMode` (`Balanced` default; `Solo` for dev/test), `ConnectionStrings:redis`
  (SignalR backplane; **unset = in-proc, single-node preserved**), optional `Cluster:NodeName` for
  diagnostics. Redis wiring is opt-in: no Redis configured → skip `AddStackExchangeRedis`.
- **Startup-migration race.** `Database:MigrateOnStartup` runs on every node (`Program.cs:50-53`); N nodes
  racing `MigrateAsync` at boot can deadlock or double-apply. Wrap it in a Postgres advisory lock
  (`pg_advisory_lock`) so exactly one node migrates and the rest wait then no-op — same spirit as the
  existing `plugin-init` one-shot.
- **Shared plugin storage.** `automatex-plugins` is one named volume. Replicas on **one host** share it;
  **across hosts** it needs real shared storage (NFS/object) — out of scope for v5.0, documented as a
  single-host-replicas constraint. Upload still writes the file once; `PluginsChanged` makes every node pick
  it up.
- **compose + docs.** `docker-compose.prod.yml`: add a `redis` service, show scaling `api` to N replicas,
  Caddy sticky sessions, advisory-locked migration. `deploy-homelab.md` gains an HA section; `.env.example`
  gets the new keys. The default compose (no replicas, no Redis) stays a valid single-node deploy.

## Slice plan

**m1 — configurable durability mode, default Balanced (backend, tests-first).**
Read `Engine:DurabilityMode` in `AddAutomateXEngine`; default `Balanced`; fixture + AppHost set `Solo`. No
behaviour change for a single node. Tests: engine boots `Balanced` against Postgres with agents not
double-registered; `Solo` still runs everything on one node (existing suite green). No migration (Wolverine
self-provisions). *Verify:* `dotnet test`.

**m2 — execution lane on a shared Postgres queue (backend, tests-first, concurrency-critical).**
Route `RunWorkflow` + step messages to `ListenToPostgresqlQueue` (auto-provisioned). Tests: a published
`RunWorkflow` is claimed once and runs to completion; two hosts on one DB never both process the same
envelope; the outbox still commits envelope + state atomically. **Subagent concurrency review.**
*Verify:* `dotnet test`.

**m3 — singletons → SingularAgents (backend, tests-first, concurrency-critical).**
Convert `CronScheduler`, `PluginTriggerHost`, `StuckExecutionSweeper`, `RetentionSweeper` to SingularAgents;
drop their `AddHostedService`. Keep `PluginMemoryWatchdog` + `PluginWatcher` per-node. Tests: with two hosts
on one DB, exactly one runs each agent; when that host stops, the other takes over; a given plugin-trigger
listener starts on one node only (no duplicate fire). **Subagent concurrency review.**
*Verify:* `dotnet test`.

**m4 — plugin reload broadcast (backend, tests-first).**
Add the `PluginsChanged` message + handler (recycle local supervisor, re-describe); publish it from the
reload/upload path. Tests: handling `PluginsChanged` recycles the supervisor; an upload publishes it.
*Verify:* `dotnet test`.

**m5 — SignalR Redis backplane (backend).**
`AddStackExchangeRedis` when `ConnectionStrings:redis` is set; no-op otherwise. Tests: the backplane resolves
when configured; the unconfigured path is unchanged (single-node still works). Cross-node fan-out is
integration-verified by Eirik on a 2-node bring-up. *Verify:* `dotnet test`.

**m6 — concurrency-safe startup migration (backend, tests-first).**
Wrap `MigrateAsync` in `pg_advisory_lock` so N booting nodes serialize; the losers wait then no-op. Tests:
two concurrent migrators against one DB don't both apply and don't throw. *Verify:* `dotnet test`.

**m7 — deploy + config + docs (compose + docs, no app code).**
`docker-compose.prod.yml`: `redis` service, `api` replicas, Caddy sticky sessions, advisory-locked
migration. `deploy-homelab.md` HA section; `.env.example` keys; `CHANGELOG` v5.0.0. *Verify:* Eirik brings
up 2 `api` replicas + Redis and confirms single-owner agents + cross-node live updates.

## Calls made

- **Balanced + the Postgres transport, no new broker.** Balanced gives leader election + failover; the
  Postgres transport gives competing-consumers execution. Postgres stays the only stateful backbone for
  durability — Rabbit/ASB add nothing here. Redis enters *only* for SignalR, which has no Postgres option.
- **Lean on Wolverine agents; don't hand-roll leader election.** `SingularAgent`/`IAgentFamily` is exactly
  this — tested, with heartbeats and reassignment. A hand-rolled advisory-lock election was considered and
  rejected as redundant risk.
- **Durability mode is config, default Balanced; `Solo` stays the dev/test path.** Keeps the single shared
  composition root (no app/test drift) and keeps the fixture fast + single-node.
- **`PluginMemoryWatchdog` + `PluginWatcher` stay per-node.** They supervise node-local processes; not every
  worker on the brief's list is a singleton.
- **Plugin hosts stay per-node.** Blast-radius stays local; only the plugin *files* are shared, handled by
  `PluginsChanged`.
- **Redis backplane + sticky sessions.** The only first-party self-hosted SignalR backplane; sticky sessions
  are still required for negotiate affinity. A Postgres `LISTEN/NOTIFY` backplane was considered and rejected
  — a DIY `HubLifetimeManager` is high risk with no upside over one Redis container.
- **No app migration for durability; but the startup migration itself must be advisory-locked** for N nodes
  (m6). Wolverine self-provisions its own schema.
- **Everything degrades to today.** `Solo` + unset Redis = the exact current single-node behaviour, so the
  arc is safe to land slice by slice and single-node users are unaffected.
- **Commit scopes:** m1–m3, m6 `feat(cluster): … (v5.0 mN)`; m4 `feat(plugins): … (v5.0 m4)`; m5
  `feat(cluster): … (v5.0 m5)`; m7 `docs(cluster): … (v5.0 m7)` (with the compose change).

## Deferred

- **Cross-host shared plugin storage** (NFS/object) — v5.0 is single-host replicas.
- **Partitioned trigger ownership** (shard listeners across nodes) — SingularAgent puts all listeners on one
  node; fine until trigger volume needs spreading.
- **Managed backplane** (Azure SignalR Service) — unneeded for self-host.
- **Agent/cluster ops UI** (Wolverine's CritterWatch) — surfacing node + agent assignment in the AutomateX
  UI is a follow-up, not a v5.0 requirement.
