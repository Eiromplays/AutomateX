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

**Security roadmap (recorded at v1.2):** Current posture (single-tenant self-host) is sound: AES-256-GCM secrets with the key outside the DB, HttpOnly sessions, fixed-time compares. Known limits: in-proc plugins are fully trusted code, the API key is one shared credential, webhooks ~~sit behind that shared key~~ (fixed in v1.3: per-trigger secrets, shown once, fixed-time validated; /api/webhooks exempt from the global gate), and one encryption key serves the whole instance. If SaaS ever becomes real, in order: (1) OIDC identities replace the shared key — already planned; (2) tenancy — reframed (v1.2 discussion) as **Workspaces**, a product feature valuable even for pure self-hosting: separate spaces for organization, invitable members with viewer/editor/owner roles. Sequencing is forced: OIDC identities first, then workspaces, then roles. Migration is small if done early — `WorkspaceId` lands on just two roots (Workflow, Connection); everything else inherits via joins. EF global query filters enforce scoping; (3) envelope encryption — per-tenant DEKs wrapped by a KMS-held master key; the `v1:` ciphertext prefix exists so a `v2:` envelope format can migrate lazily; (4) out-of-proc plugin sandboxing via the existing json-in/json-out executor seam; (5) audit log + rate limiting. HTTPS via Caddy's automatic TLS at deployment.

**Connections decisions (recorded at v1.2):** Connection-resolved values are **masked** (`***`) in persisted outputs/errors and published events, GitHub-Actions-style (best-effort: exact + JSON-escaped forms; transformed values can't be recognized). Secret values are **write-only** — never returned by API or UI after creation (GitHub Actions model; "secret values never leave the server" stays a one-sentence guarantee, and no reveal-permission tier is ever needed). UX is served instead by key names being visible and `PUT /api/connections/{id}` (delivered in v1.3) with merge semantics: provided keys overwrite, `null` deletes, absent keys untouched — rotate one field without re-entering the rest. The name "Connection" (not Secrets/Credentials) is deliberate: these grow into OAuth-backed third-party connections per the roadmap, and the `{{connections.…}}` template root is the one rename that would break user configs.

**Live plugin platform (v2.4):** the hot-reload + workspace-plugins + upload trio, delivered on the deferred designs above. Decisions: reload is **three atomic swaps in dependency order** (assemblies → action registry → event bus) behind one `PluginReloader` lock; in-flight executions drain on their original executors because collectible ALCs don't collect while referenced — graceful handover came free from the M2 ALC choice. Workspace plugins live in `plugins/.workspaces/<id>/` (dot-dirs reserved), resolve workspace-first via `ActionSnapshot` (encoded in ActionResolutionTests), and contribute **actions only, never event listeners** — engine events are instance-wide and a workspace listener would observe other tenants. Upload is zip-of-publish-output gated behind `Engine:AllowPluginUpload` (default off; it is RCE by definition) with zip-slip validation before any disk write (PluginArchiveTests); global upload = any authenticated caller, workspace upload = that workspace's Owner. Known limits, accepted: workspace scoping is policy, not isolation (in-proc code can read anything — out-of-proc sandboxing remains the real boundary); replacing a loaded plugin's files may fail on Windows hosts (file locks); an instance-admin role becomes necessary before any untrusted-tenant scenario. Rider: `http.request` v2 (headers/contentType/timeout/failOnErrorStatus/response headers — found the gap trying to query a private GitHub repo: no way to send Authorization) and `matrix.send` `msgType` (m.notice = bot etiquette, and a visible canary diff for workspace-shadowing tests). One stated behavior change: request bodies default to application/json, was text/plain. **Hot-reload bug found by dogfooding and fixed:** the CoreCLR PE-image cache is keyed by absolute path, so a new ALC loading a replaced DLL from the original path served the *old* image until process restart. Fix: **shadow copy** — every load copies the plugin folder to a unique temp path (PluginShadowCopyTests); also removes Windows file locking on replace. Plugin listings now expose the assembly MVID as a build fingerprint, making "what code is actually loaded" observable; uploads report the fingerprint transition (updated / unchanged / installed) through app-wide toasts. **Delete guard (v2.5):** removing a plugin whose actions appear in any workflow's *latest* version is refused with the workflow names unless `force=true` — latest-only because history is pinned and only future executions reach for current code (`PluginUsageTests`). **Drift warnings:** config keys absent from the active action's schema are flagged amber in the builder and on workflow steps — values are preserved but silently ignored at execution (the msgType-on-old-plugin lesson). The release workflow carries a self-deploy webhook step, inert until the `AUTOMATEX_DEPLOY_WEBHOOK` secret exists. The same in-use guard protects **connections** (`ConnectionUsageTests`): deletion is refused while a latest version references `{{connections.<name>.…}}` — regex anchored on the trailing dot so `deploy` never matches `deployment`. API error envelopes are unwrapped client-side, so confirms and toasts show sentences, not JSON. **Conditionals / gates (v2.9):** the `gate` built-in action — the deterministic branching primitive. Compares a (templated) value (`equals`/`notEquals`/`contains`/`isTruthy`, AND-ed; default = truthy); a **closed gate halts the workflow cleanly** — remaining steps recorded `Skipped`, execution still `Succeeded` (chosen semantics: stopping the chain is normal flow, not failure). Condition logic is a pure `GateEvaluator` (unit-tested); the halt + skip-recording lives in `ExecuteStepHandler` (`step.ActionType == gate && Gate.IsClosed(output)` → enumerate later steps, `AddSkippedStep`, complete). New `ExecutionStatus.Skipped` (step-level only). Engine tests pin closed-skips-and-succeeds + open-continues. Unlocks "check-and-act-if" with zero AI (`docs/recipes/conditional-gate.md`); pairs with cron/ticker for "check every N min, act only if…". Note: a gated execution Succeeds, so chains on "succeeded" still fire — chain off "failed" for un-gated-only.

**Trigger edit (v2.9):** triggers were add/delete only — now editable. `PUT /triggers/{id}` updates config (cron re-parses + recomputes nextRunAt, workflow re-validates target+on, plugin triggers re-validate and the host auto-restarts on the config-fingerprint change) and/or toggles `enabled`. Webhook config is immutable (`TriggerEditRules.CanEditConfig` — its config holds the hashed secret; rotate instead). UI: each trigger row gets Enable/Disable + an inline config editor (cron input / workflow selects / plugin SchemaForm). `TriggerEditRulesTests` pins the rules.

**v2.9 "convergence" (in progress — the big pre-v3 release).** Bundles matrix.onMessage (done) with: scheduled workflows (done), connection types, SignalR workspace groups, plugins-page tabs, refresh-token session renewal, and **conditionals/gates** (the deterministic branching primitive — `filter`/`gate` step that halts the workflow unless a template expr is truthy; unlocks "check-and-act-if" without AI). Each ships as its own commit; the release is tagged once when the bundle lands. Items below move up into this release as built.

**Scheduled workflows (v2.9):** `schedule.workflow` — a privileged *built-in* action (not an SDK plugin: it needs `IMessageBus` + DbContext) exposing Wolverine's durable scheduling. A step queues a future run of another workflow (`delaySeconds` or `runAt`, optional payload), `triggeredBy: scheduled`, one-shot (fires once, no cleanup), workspace-bound. Timing rules unit-tested (`ScheduleResolutionTests`), firing integration-tested. The composed payoff is `docs/recipes/reminders.md`: Matrix message → `llm.prompt` extracts `{delaySeconds, message}` → `schedule.workflow` queues a deliverer → it fires on time. Natural-language reminders, fully self-hosted and durable. Generalizes to any "do X later".

**Refresh-token session renewal (v2.9, conceded):** the earlier "no refresh needed" take was too narrow — it answered "call APIs as the user?" (no) not "stay logged in without a redirect?" (yes, worth it). `SaveTokens=true` + `offline_access` scope + refresh-on-validate in the cookie pipeline renews the session server-side from the stored refresh token — no bounce, standard pattern. (Silent `prompt=none` re-auth is the redirect-based alternative; refresh tokens are smoother.)

**Architectural decision — orchestration engine, not agent runtime (recorded v2.9):** the core stays deterministic/durable/auditable primitives (triggers, actions, chaining, scheduling). Open-ended LLM reasoning (think→act→observe loops) is deliberately kept OUT of the engine — it would break determinism and turn the execution log into fiction. "Do complex thing X" requests decompose into two needs, homed separately:
  1. **Conditional logic** ("remind me *when* found", "notify *if* balance < X") — NOT agentic; it's the missing **branching/gate** primitive (workflows are currently linear, every step runs). A deterministic `filter`/`gate` built-in (halt unless a template expr is truthy) unlocks most "check-and-act-if" automations with zero AI. Belongs in core; high value; fully testable. (Was "DAG/branching" on the post-v1 list.)
  2. **Open-ended reasoning** — agent territory, lives at the EDGES: a bounded `llm.agent` *plugin* (max iterations, curated tools, cost ceiling) near-term; **MCP both directions** long-term — AutomateX as MCP *server* (workflows as tools for Claude/agents) + an `mcp.call` *action* (the whole MCP tool ecosystem as workflow steps). MCP is the proper home for "AutomateX gets smart."

**Connection types (v2.9):** delivered — `[ConnectionType("matrix","Matrix")]` + `IConnectionType` returning labeled `ConnectionField`s (secret/plain, required, help text, docs link). Discovered from host + global plugins (`ConnectionTypeRegistry`, rebuilt on reload — installing a plugin teaches the UI its connection shape), surfaced at `GET /api/connection-types`. The connections form gained a Type picker: a known type renders guided fields (labels, "where to get it ↗" links, password-vs-text by secret flag, required markers); "Custom" keeps the free-form key/value editor. The `provider` field is the type key, so it's fully backward compatible (old/unknown providers → Custom). Storage unchanged — all values stay write-only + encrypted; `Secret` is a UI hint. Matrix/Llm/Ssh plugins ship their types. **Docs-link principle:** link our own recipes, or a vendor only when the type is single-vendor (Matrix→recipe; LLM is multi-vendor so no OpenAI-specific link). (Future: returnable non-secret connection fields would need a schema change; deferred.)

**Connection-types follow-ups (recorded):**
- **Plugins page shows connection types** too (fold into the plugins-tabs task — a tab/section beside actions + triggers).
- **Connection-reference helper (v2.9):** delivered — `SchemaForm` text fields get a 🔗 dropdown (connections grouped by name → keys) that inserts the exact `{{connections.<name>.<field>}}` token, plus a "🔗 connection" chip on the label when a field references one. Generic over all string inputs. Plugins page also gained tabs (Installed/Actions/Triggers/Connections), per-tab counts + search, 2-col grids, full-width layout.
- **OAuth connection types + field provenance (big future arc — was "OAuth connection manager UI" on post-v1):** a plugin declares an OAuth connection type (authorize/token endpoints, scopes); the engine runs the flow + refresh and *populates* fields. This needs **field provenance** — user-entered vs **system-managed** (read-only display, the user views but can't overwrite an access token the system fetched), plus possibly an account-id/expiry the flow returns. Bigger than a field template: interactive, stateful, a redirect/callback surface. Proper dedicated release (v3-ish). The current `IConnectionType` is the seam it grows from.

**Backlog (post-v2.9 ideas, recorded so they don't evaporate):**
- **SignalR workspace groups (v2.9):** delivered — clients call `JoinWorkspace`, validated via `WorkspaceAccess.GetRoleAsync` (only members join `workspace:<id>`; open/apikey = everyone). The listener routes each event to the execution's workspace group (resolved by a cheap `WorkspaceId` lookup — no SDK contract change) and ships the **full already-masked record** instead of a bare id. Net effect: a client only ever receives its own workspace's events (no more cross-workspace refetch storms), and the executions list patches a row's status in place from the event rather than refetching — detail still refetches the viewed execution (events don't carry step attempts/timestamps). Group membership is per-connection, so the hook rejoins on every (re)connect. Camelcase note: SignalR's `JsonSerializerDefaults.Web` makes record properties arrive camelCase on the client.
- **Auth session polish** (NOT refresh tokens): current `SaveTokens=false` + 8h sliding cookie is standards-correct for an auth-only app — nothing to refresh because AutomateX never calls downstream APIs as the user. Worth doing: configurable session length + `prompt=none` silent re-auth so idle expiry is a seamless redirect, not a visible bounce. Refresh tokens become necessary *only if* a "call GitHub/Graph as the signed-in user" feature lands (then `SaveTokens=true`).
- **Plugins page tabs (v2.9):** delivered — `/plugins` is now a tabbed hub: *Installed* (global/workspace mgmt + catalog), *Actions* (live count), *Triggers*, *Connections* (connection types with guided fields). Reload in the header. Pure frontend.
- **A forgotten one** (Eirik, one sleep ago): if it resurfaces, it goes here.

**The ears (v2.9):** `matrix.onMessage` — a sync long-polling listener and the first real consumer of the trigger SDK. Rules: the bot's own messages **never** fire (unconditional loop protection, so reply workflows can't self-trigger); history before listener start is skipped (in-memory since-token, restarts don't replay); optional room filter; sync failures throw into the supervisor's backoff (`MatrixOnMessageTriggerTests`). Enabler shipped with it: **trigger configs resolve `{{connections.…}}` at listener start** (workspace = the workflow's; stored rows and the UI keep the template — secrets never persist in trigger configs), pinned by an engine test. The composed result is `docs/recipes/jarvis-lite.md`: Matrix message → local LLM → reply, fully self-hosted. Listener payloads are the plugin's responsibility — like actions, they shouldn't echo secrets.

**The plugin platform, finished (v2.8):** the last closed extension point opens — **trigger plugins**: `[Trigger]` + `ITriggerListener<TConfig>` in the SDK (long-running `RunAsync`, `context.FireAsync(payload)`; clean return = poll cycle, re-run after delay; throw = restart with backoff). `PluginTriggerHost` supervises one listener per enabled trigger row, reconciling on `Engine:TriggerSyncInterval` — config edits, disables and hot-reloads (registry generation) all restart listeners cleanly. Trigger types come from **global plugins only** (the event-listener rule extended: workspace plugins contribute actions, never instance-wide machinery). Config schemas export like actions, so the trigger UI renders real forms; `sample.ticker` is the hello-world. And the **catalog**: CI packages every `src/Plugins/*` as release-asset zips plus `catalog.json` (stable `releases/latest/download` URL) with sha256 per entry; `POST /api/plugins/catalog/install` downloads, **verifies the hash before any disk write**, extracts and hot-reloads — same `AllowPluginUpload` trust gate. The lean-core vision delivered: first-party plugins one click away (`TriggerDiscoveryTests`, `PluginTriggerHostTests`, `PluginCatalogTests`). The Jarvis path is now open: a Matrix-sync listener plugin would make AutomateX conversational.

**Execution retry (v2.7):** `POST /executions/{id}/retry` replays the **byte-identical** original trigger payload — provenance rides `triggeredBy: "retry:<id>"`, never the payload, so templates see exactly what the first run saw — on the **latest** version (the dominant story: failed → fix the workflow → retry the data; the original stays pinned for comparison). Terminal-only; Succeeded included ("Run again"). Chains fire normally on the retried run (`ExecutionRetryTests`).

**Export/import (v2.7):** portable workflow documents (`automatex: 1` format version) — name, description, latest-version steps with inline JSON configs, **cron triggers only**. Webhook triggers (secrets) and chain triggers (instance-local ids) are excluded by construction; connections travel as name references, so the importing instance needs same-named connections. Import validates format, action-type availability (install plugins first — named in the error) and crons; creates a fresh v1 (`WorkflowTransferTests`). **Future ideas recorded:** plugin catalog/installer (CI release-asset zips + static catalog.json with sha256, install = download→verify→existing Extract+reload, gated by the same AllowPluginUpload flag — lean core image, first-party plugins one click away); trigger plugins (the one extension point still closed to the SDK); plugin settings deferred (a shared connection covers the need without a third config tier).

**llm.prompt (v2.7):** third first-party plugin — one OpenAI-compatible chat-completions action covers OpenAI, OpenRouter, Ollama, LM Studio and vLLM via `baseUrl`. Decisions: `apiKey` optional (local endpoints need none; Bearer only when present, per-request); sampling params forwarded only when set; non-2xx fails the step with the provider's error body (and retries deliberately re-bill — an LLM call is environmental, same ladder as everything else). Unlocks the composed showcase: webhook → chain → `llm.prompt` summarizes → `matrix.send`.

**Workflow chaining (delivered v2.6):** a `workflow` trigger type ("when workflow X succeeds/fails/any") with the source execution as payload and a chain-depth cap (`Engine:MaxChainDepth`, default 5; over-cap = logged skip, not failure). One design upgrade over the original sketch: instead of a best-effort event listener, chained `RunWorkflow` messages are **returned as Wolverine cascades from the step handler**, committing through the same EF outbox as step messages — chains inherit crash-safety for free (`WorkflowChainingTests`). Source context propagates (`source.triggerPayload` nests the watched execution's own payload), workspace boundaries are enforced at both creation and fire time, and sweeper-failed (stuck) executions deliberately don't chain. Steps within a workflow are already strictly sequential by design (message-per-step cascade) — chaining is about composing workflows, not ordering steps. **Also recorded: workflow-scoped connections** — a nullable `WorkflowId` on Connection narrowing resolution to one workflow (blast-radius reduction). Marginal for self-hosting given workspace scoping + masking already contain secrets; earns its complexity only alongside finer-grained sharing needs.

**Workflow lifecycle (v2.3):** edit = append-an-immutable-version (the M1 versioning design finally exposed in the UI; engine tests pin that history keeps its version + outputs while new runs use the latest). Delete workflow = full cascade including execution history — executions are deliberately not FK-bound to workflows, so `WorkflowDeletion` removes them explicitly and atomically. Delete execution = terminal-only (`ExecutionDeleteRules`): a Running execution has durable-inbox messages that would resurrect or write to ghost rows. Roles: both deletes are Editor — same as create, destructive ≠ administrative. Version **restore** follows the same philosophy: git revert, not git reset — `RestoreVersion(n)` appends a copy of vN's steps as the newest version (domain method, unit-tested), restoring the latest is rejected as a no-op, and nothing is ever repointed, so execution pinning holds by construction. **Deferred (wanted):** plugin hot-reload — collectible ALCs make it natural (in-flight executions pin the old ALC until they drain; new executions resolve from the reloaded catalog); the safe shape is an explicit `POST /api/actions/reload`, not a file watcher. Pairs with the workspace-scoped plugin future above.

**First-party plugins (v2.2):** `ssh.command` + `matrix.send` ship in-repo under `src/Plugins` — the first SDK consumers written from the outside in, and the engine of the self-deploy recipe (`docs/recipes/self-deploy.md`). Decisions: a non-zero exit code **fails the step** (remote commands are environmental, so the normal retry ladder applies); `matrix.send` transaction ids are deterministic per execution+step, leaning on Matrix's txn dedupe for at-most-once notifications under engine retries; host-key pinning is opt-in (`hostFingerprint`) because first-connect ergonomics matter for self-hosters; and the self-deploy hardening lives on the *host* (forced-command authorized_keys + detached update script), not in the plugin — the platform can't distinguish a deploy from an attack, the host can. The detached script also sidesteps the suicide problem: the execution completes before the restart kills the process that ordered it. UI surfaces action provenance (source already on `ActionDescriptor` since M2): builder dropdown grouped by origin, plus an Actions catalog page. **Future (recorded):** workspace-scoped plugin allowlists — cheap to enforce at workflow-create + engine dispatch, but honest isolation only arrives with out-of-proc plugins (already on the security roadmap); until then an allowlist is policy, not a boundary, because in-proc plugin code can read anything the host can.

**Workspaces delivered (v2.1):** as designed — Workspace + members (email invites, subject bound on first sign-in), viewer/editor/owner, `WorkspaceId` on the two roots plus denormalized onto Executions for cheap history scoping and engine-side connection isolation. Implementation choices: `X-Workspace-Id` header context (not route nesting), explicit endpoint filters (not EF global filters — the engine must see all workspaces), zero-member workspaces open to any authenticated user (bootstrap; first member claims), last-Owner guard, SignalR slimmed to execution ids for cross-workspace privacy. Open/apikey modes: everyone is Owner — workspaces are folders. **Invite model decision:** auto-membership (Google Drive/Notion style), not accept/reject ceremony (Slack style) — membership grants rather than demands, and "reject" is fully served by self-service leave (anyone may remove themselves; last-Owner guard applies). Visibility instead of consent: the UI surfaces newly-appeared memberships with a switch/dismiss banner. Accept/reject states earn their complexity only if this ever serves strangers (SaaS).

**Auth delivered (v2.0):** API-side OIDC exactly as decided below — `AddOpenIdConnect` + cookie scheme, `/auth/login` challenge, forwarded headers for proxied redirect URIs. One stated deviation: the API key was **demoted, not deleted** — it remains the machine-client credential (`X-Api-Key`) and the no-IdP fallback for self-hosters; the browser key-cookie exchange only applies in API-key mode. Auth is a tri-state: open → apikey → oidc.

**Auth decision (recorded at v1):** v1 ships an optional shared API key; the browser exchanges it for an HttpOnly cookie via `/api/auth/session` — deliberate scaffolding, deleted when Entra lands. For the Entra pass, the front-runner is **API-side OIDC** (`AddOpenIdConnect` + cookie scheme): the Caddy proxy makes browser↔API same-origin, so the API can own the OIDC flow with standard ASP.NET middleware — BFF-grade security (no tokens in the browser) without flipping the SPA to SSR or adding a Node BFF. Alternatives if that disappoints: MSAL.js PKCE in the SPA (static stays, tokens in browser) or SSR/Node BFF (only if SSR is wanted for other reasons). If API keys survive long-term alongside Entra, revisit where session minting lives.

---

## 12. How to not stall (lessons from v1)

- **Cap the surface.** Every "what if it also…" goes in the Post-v1 list, not v1.
- **Dogfood from M1.** If you're not running it on something real by M1/M2, the scope is wrong.
- **The engine is the project.** When in doubt, invest in the durable runner and the SDK ergonomics, not breadth of integrations or UI polish.
- **One deployable, always.** Any time you're tempted to split a service, write a module instead.

---

*Constraint respected: this plan is a new document; nothing in the existing AutomateX repo was modified.*
