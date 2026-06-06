# AutomateX v2 — Architecture & Scope

**Status:** planning draft v0.2 (revised after review) · **Target:** .NET 10 (LTS) / Aspire 13 · **Date:** June 2026

---

## TL;DR

Rewrite AutomateX as a **single, self-hostable .NET-native automation engine** — a modular monolith API + a separate static SPA, one Postgres database, started with one `docker compose up`. The product's spine is *being the rare fast, AOT-friendly automation engine in a Node/TS-dominated field*, with a clean **first-class C# plugin SDK**. The v1 success criterion is not "better architecture" — it's **"the smallest thing I'd genuinely run, finished and dogfooded."** Everything below optimizes for that.

The single biggest change from v1: collapse 5 services + 4 databases + RabbitMQ + InfluxDB down to **one deployable + one Postgres**. That alone delivers most of "easier to set up and host," and makes the project finishable solo.

---

## 1. Why a rewrite (not a port)

v1 was architecturally clean at the *slice* level (FastEndpoints + MediatR VSA — good) but the *macro* architecture was a distributed system built for a team: `UserService`, `PluginsAPI`, `ThirdParties`, the core engine, a separate `Discord.Bot`, four Postgres databases, RabbitMQ/MassTransit, InfluxDB, three plugin SDKs. That's the direct cause of "hard to host" and "hard to come back to" — the two things that kill a solo side project.

So v2 keeps the *idea* and the *good VSA instincts*, and throws away the *topology*.

---

## 2. The spine: a .NET-native engine

The entire competitive field (n8n, Activepieces, Windmill, Make, Zapier) is Node/TS or closed SaaS. A fast, self-hostable engine where **the engine itself is the product and plugins are idiomatic C#** is genuinely uncommon and a strong portfolio signal. Concretely this means:

- The execution engine, durable state, retries and scheduling are the showpiece — not the node canvas.
- Plugins are C# packages with a tiny, well-designed SDK. Authoring a new trigger or action should be ~30 lines and obvious.
- AOT-friendly, single-binary, trivially self-hostable. "It's the automation engine that runs as one fast container" is the pitch.
- MCP is added later as *one more trigger/action surface*, not the identity — ride the agent wave without competing on integration count.

---

## 3. Architecture: modular monolith

One ASP.NET Core process, **Vertical Slice Architecture** internally (matches how you already work — no Clean Architecture layering). Modules are folders/namespaces with their own slices, not separate deployables.

```
AutomateX (single process)
├─ Modules/
│  ├─ Workflows      (define + version workflows)
│  ├─ Triggers       (inbound: cron, webhook, github, ...)
│  ├─ Actions        (outbound: http, script, notify, ...)
│  ├─ Executions     (run state, history, logs)
│  ├─ Plugins        (load + host C# plugins)
│  ├─ Connections    (3rd-party credentials/OAuth, encrypted)
│  └─ Identity       (auth — keep it boring, see §8)
├─ Engine/           (the durable execution core — §6)
└─ Host/             (ASP.NET, FastEndpoints, Wolverine, SignalR, OpenAPI)

Web (separate deployable): React Router v7 SPA — pure static bundle
talking to the API. Kept separate so people can run API-only or roll
their own frontend (§10).

Infra: ONE Postgres database (schema-per-module if you want isolation).
Local dev: Aspire 13 AppHost (Postgres + API + web).
Prod: API container + web container + a Postgres connection string.
```

**Aspire's role flips:** in v1 it orchestrated a fleet of services. In v2 it's a *local-dev convenience* (spin up Postgres + the app + dashboard/telemetry). Production is a single image — Aspire is not required to run it.

---

## 4. 2026 stack picks

Several libraries v1 relied on went commercial in 2025. Pick free, source-generated, .NET-native replacements — this also reinforces the engine thesis.

| Concern | v1 | v2 recommendation | Why |
|---|---|---|---|
| Mediator / messaging | MediatR + MassTransit | **Wolverine** (the v2 experiment) | MediatR and MassTransit both went commercial in 2025. Wolverine is free (MIT), source-generated, and bundles CQRS *and* messaging *and* durable outbox/retries/scheduling/sagas — i.e. it *is* a chunk of your execution engine. Unproven for us: evaluate it in M0/M1; fallback is plain DI handlers + a small hand-rolled Postgres queue worker (FastEndpoints stays either way). |
| Object mapping | AutoMapper | **Mapperly** (or hand-map) | AutoMapper went commercial (Apr 2025). Mapperly is compile-time, zero-overhead. In VSA, just hand-map in the slice — usually no mapper needed. |
| HTTP API | FastEndpoints | **FastEndpoints** (keep) | Free, fast, VSA-friendly. Pairs well with Wolverine handlers. |
| ORM | EF Core | **EF Core 10** only | Migrations + LINQ everywhere; no Dapper — `Database.SqlQuery<T>()` / `ExecuteSql` covers hot paths like queue polling if profiling ever demands raw SQL. |
| Background/queue | RabbitMQ | **Postgres** (Wolverine transport + `FOR UPDATE SKIP LOCKED`) | No separate broker to host. Add a real broker only if you ever need cross-node fan-out. |
| Metrics/history | InfluxDB | **Postgres** (+ OpenTelemetry to Aspire dashboard in dev) | Execution history is relational; drop the time-series DB entirely. |
| Realtime UI | SignalR | **SignalR** (keep) | Live execution status. |
| Frontend | Next.js 14 | **React Router v7 (framework mode, SPA) + React 19** | "Remix" in 2026 *is* React Router v7 — the planned Remix v3 shipped as RRv7 (Dec 2024); the current "Remix 3" is an experimental React-less rewrite on a Preact fork, not production material. RRv7 SPA mode = static bundle, deployed separately from the API. shadcn/ui + TanStack Query/Table stay. |
| Plugin SDKs | C# + TS + Python | **C# only** | One SDK, done well. Re-add others only if there's demand.

---

## 5. Core domain model (v2)

Keep it small and explicit. A **Workflow** is a versioned graph of **Steps**. A Step is either bound to a **Trigger** (entry) or an **Action**. A run is an **Execution** with per-step **StepExecution** state.

```
Workflow ──< WorkflowVersion ──< Step ──> (Trigger | Action) binding
                                   │
Execution ──< StepExecution        └─ config (jsonb) + input/output mapping

Trigger   { type, config:jsonb }          // cron, webhook, github, ...
Action    { type, config:jsonb }          // http, script, notify, ...
Connection{ provider, encrypted secrets } // OAuth/tokens, per-user or global
Plugin    { id, version, manifest }       // contributes trigger/action types
```

Notes:
- **Version workflows** (immutable `WorkflowVersion`); executions pin a version. This is cheap to add now and painful to retrofit later.
- `config`, `input`, `output` are `jsonb`. Validate against a per-type schema declared by the plugin.
- **Drop SmartEnum in v2.** Trigger/action types become **plugin-contributed string ids** (`"http.request"`) — there's no central enum to maintain, and the behavior that justified SmartEnum now lives in the plugin class. For genuinely closed sets (`ExecutionStatus`, `PluginScope`), plain enums stored as text (`HasConversion<string>()`) + `JsonStringEnumConverter` on the API: readable in DB and JSON, no EF complex-property ceremony, no Postgres-native-enum `ALTER TYPE` migration friction. New values are a code change, not a migration.

---

## 6. The execution engine (the showpiece)

This is what makes it a .NET engine worth showing. Design it as **durable, resumable, at-least-once** from day one — it's the hard, interesting part and the thing v1 never nailed.

Run loop:
1. A trigger fires → enqueue an `Execution` row (`status=Pending`) in Postgres (transactional outbox via Wolverine).
2. Worker(s) claim work with `SELECT ... FOR UPDATE SKIP LOCKED` (no external broker needed).
3. Execute steps sequentially (v1) / DAG (later). Persist each `StepExecution` result before advancing → **resumable** after a crash/restart.
4. **Retries with backoff** per step (Wolverine gives you this); **idempotency keys** on actions so replays don't double-send.
5. Stream status to the UI over SignalR; write full history to Postgres.

**Saga note:** v1-you would have modeled this as a MassTransit saga state machine. Wolverine has sagas too, and trying them for the runner is a legitimate part of the Wolverine experiment — but either way, `Execution`/`StepExecution` stay *your* tables that the saga merely drives. If the framework owns the engine state, the engine stops being the product (and you can't swap the framework later).

Deliberately *not* in v1: distributed workers, compensation, branching/loops, sub-workflows. The single-process durable runner is enough to be impressive and usable. Leave seams for the rest.

---

## 7. The C# plugin SDK (the differentiator, made concrete)

Authoring a trigger or action should be tiny and discoverable. Target shape:

```csharp
[Action("http.request", "HTTP Request")]
public sealed class HttpRequestAction : IAction<HttpRequestConfig, HttpResult>
{
    public async Task<HttpResult> ExecuteAsync(
        HttpRequestConfig cfg, ActionContext ctx, CancellationToken ct)
    {
        var res = await ctx.Http.SendAsync(cfg.ToRequest(), ct);
        return new HttpResult((int)res.StatusCode, await res.Content.ReadAsStringAsync(ct));
    }
}
```

- **Config types are the schema.** Generate the UI form + JSON schema from the C# config type (source generator or reflection) — no hand-written form per action. This replaces v1's Reinforced.Typings round-trip.
- **Loading:** in-process via `AssemblyLoadContext` (collectible, isolated) for v1. Plugin = a NuGet-style package with a manifest. Path to **out-of-process** (gRPC/stdio) later for untrusted plugins — design the `ActionContext`/`TriggerContext` boundary now so it's swappable.
- **Plugins can listen, not just define.** Engine events (`ExecutionStarted`, `StepCompleted`, `ExecutionFailed`, …) are published on the in-proc message bus; a plugin subscribes by declaring a handler (e.g. `IListenFor<ExecutionCompleted>`). This replaces v1's socket-based event SDK with a one-interface story, and maps cleanly onto an out-of-process transport later.
- **Script execution** (the v1 Dockerized script runner) becomes the canonical "untrusted code" plugin: run user scripts in a sandboxed container, not in-proc.
- One SDK, with a `dotnet new automatex-plugin` template and a sample plugin in the repo.

---

## 8. v1 trigger/action set (default — "decide later")

Ruthlessly small, but enough to dogfood real automations. ~3 triggers, ~4 actions:

**Triggers**
- `cron` — scheduled (Cronos/Quartz-style parsing).
- `webhook` — inbound HTTP endpoint per workflow.
- `github` — repo webhook events (you already had this; high signal, easy demo).

**Actions**
- `http.request` — call any endpoint (the universal escape hatch).
- `script.run` — sandboxed script (container) — your original strength.
- `notify` — one action, multiple targets: Slack / Discord / email / Pushover (don't build four).
- `transform` — map/template the payload between steps (tiny, but you need it constantly).

This set covers a huge range of real homelab/DevOps automations, which is almost certainly what you'll actually run. Email/Slack/Discord/SSH from v1 become plugins added post-v1.

---

## 9. What survives vs what dies

| v1 | v2 | Notes |
|---|---|---|
| VSA slices (FastEndpoints + handlers) | **Keep** | Good instinct; carry it forward. |
| Smart-enum type pattern | **Die → string type ids + plain enums** | Plugin-contributed string ids for open sets; plain enums as text for closed sets (§5). |
| Visual draft-builder (dnd-kit, react-d3-tree, Monaco) | **Keep, simplify** | Strong UX; rebuild on React 19. v1, a clean linear/step list is fine before full DAG canvas. |
| GitHub trigger, script runner, notifications | **Keep** | Core of the v1 set. |
| 5 microservices | **Die → modular monolith** | Biggest single win. |
| 4 Postgres DBs | **Die → 1 DB** | Schema-per-module if you want isolation. |
| RabbitMQ / MassTransit | **Die → Postgres queue (Wolverine)** | No broker to host. |
| InfluxDB | **Die → Postgres** | History is relational. |
| Separate Discord bot service | **Die → plugin** | Discord is just a trigger/action source. |
| TS + Python plugin SDKs | **Die (for now) → C# only** | Re-add on demand. |
| MediatR / AutoMapper | **Die → Wolverine / Mapperly** | Both went commercial in 2025. |
| Custom auth + UserService | **Die → Entra ID (OIDC)** | API validates Entra-issued JWTs; SPA does auth-code + PKCE. Keep the OIDC config generic so other self-hosters can point it at Keycloak/Zitadel/etc. Don't build user management again. |

---

## 10. One-command host story

The bar to clear: a stranger self-hosts it in under five minutes.

```bash
docker compose up   # postgres + automatex-api + automatex-web
```

- **Two images, deliberately separate:** `automatex-api` (the engine + OpenAPI) and `automatex-web` (static RRv7 bundle behind Caddy/nginx). API-first means people can run backend-only or roll their own frontend.
- API-only path: `docker run ghcr.io/eiromplays/automatex-api -e ConnectionStrings__Db=...`.
- Images built via Aspire publish / `dotnet publish /t:PublishContainer` — no hand-written Dockerfiles to maintain.
- EF migrations applied idempotently on API startup; config via env vars only; no required external services beyond Postgres (+ your Entra app registration).
- Aspire AppHost is the **local dev** experience (telemetry dashboard, Postgres container, web dev server) — not a production requirement.

---

## 11. Milestones — ruthlessly small (the anti-"never finish" plan)

Each milestone is independently shippable and dogfoodable. **Definition of done for v1 = M4.** Resist adding scope before M4 ships.

- **M0 — Walking skeleton (weekend):** Aspire AppHost + app + Postgres. One hardcoded workflow runs end-to-end (cron → http.request) and persists an Execution. Proves the engine loop.
- **M1 — Durable engine:** StepExecution persistence, retries/backoff, SKIP LOCKED worker, execution history API. The showpiece works and survives restarts.
- **M2 — Plugin SDK:** `IAction`/`ITrigger` contracts, AssemblyLoadContext loading, config-type→schema, `dotnet new` template + sample plugin. The v1 action/trigger set reimplemented *as plugins*.
- **M3 — UI:** React Router v7 SPA — workflow list, step editor (linear, not full canvas yet), live execution view over SignalR, auto-generated config forms from plugin config types.
- **M4 — Host & ship:** API + web images via Aspire publish / `PublishContainer`, compose file, migrations-on-start, Entra ID auth wired, README quickstart, encrypt Connections. **Tag v1, write a blog post, self-host it for something real.**

Post-v1 (only if still fun): DAG/branching, MCP trigger+action, out-of-process plugins, OAuth connection manager UI, more plugins, multi-node workers.

---

## 12. How to not stall (lessons from v1)

- **Cap the surface.** Every "what if it also…" goes in the Post-v1 list, not v1.
- **Dogfood from M1.** If you're not running it on something real by M1/M2, the scope is wrong.
- **The engine is the project.** When in doubt, invest in the durable runner and the SDK ergonomics, not breadth of integrations or UI polish.
- **One deployable, always.** Any time you're tempted to split a service, write a module instead.

---

*Constraint respected: this plan is a new document; nothing in the existing AutomateX repo was modified.*
