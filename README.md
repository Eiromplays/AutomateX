# AutomateX

[![CI](https://github.com/Eiromplays/AutomateX/actions/workflows/ci.yml/badge.svg)](https://github.com/Eiromplays/AutomateX/actions/workflows/ci.yml)

Self-hostable, .NET-native automation engine. This is the v2 rewrite — architecture and scope live in [docs/v2-plan.md](docs/v2-plan.md). v1 is archived at [AutomateX-v1](https://github.com/Eiromplays/AutomateX-v1).

**Status: v1.3 — connections & webhook polish.** Connections gain `PUT /api/connections/{id}` with merge semantics (filled = overwrite, removed = delete, untouched = keep — rotate one field without re-entering the rest; names immutable) and an edit form in the UI. Webhook triggers get **per-trigger secrets**: generated server-side, shown exactly once at creation (`/api/webhooks/{id}?secret=…` or `X-Webhook-Secret`), validated fixed-time — and `/api/webhooks` moves *outside* the global API-key gate, so third-party senders never hold the instance key. Webhook triggers created before v1.3 must be recreated to obtain a secret.

Previous (v1.2): Secrets move out of step configs into named **connections**: AES-256-GCM-encrypted bundles (master key from `Encryption__Key`, never stored in the database) referenced via `{{connections.<name>.<field>}}` — decrypted only at execution time, never persisted resolved, never returned by the API (key names only). Plus opt-in execution retention (`Engine__ExecutionRetention`, e.g. `30.00:00:00`) so old outputs don't accumulate forever. Connection secrets are **masked** (`***`) in everything persisted or published — step outputs, error messages, live events — GitHub-Actions-style. Best-effort by definition: transformed secrets (base64'd, split, re-encoded by the action) can't be recognized, so still don't deliberately echo them. Losing the encryption key makes stored secrets unrecoverable.

Previous (v1.1): Step configs are templates: `{{trigger.payload.x}}` carries webhook/manual JSON bodies into steps, `{{steps.0.output.y}}` pipes prior step outputs forward with JSON types preserved, and template errors fail the step instantly with a precise message (no retries — deterministic errors don't earn them). `ActionContext` now carries execution metadata. See **Data flow between steps** below.

Previous (M4): Self-hostable with `docker compose up`: the API ships as an SDK-built container (`dotnet publish -t:PublishContainer`), the SPA as a Caddy image serving the static build and reverse-proxying `/api` + `/hubs`, Postgres alongside. Plugins load from a volume-mounted `plugins/` folder, migrations apply on startup (`Database__MigrateOnStartup=false` to opt out), and an optional API key (`Auth__ApiKey`) gates `/api` and `/hubs` — API clients send `X-Api-Key`; the UI signs in once via the ⚿ button and holds an HttpOnly SameSite=Strict session cookie (the key never sits in JS-readable storage or URLs, and the cookie authenticates the SignalR handshake).

Previous (M3): React Router v7 SPA in `src/web` (React 19, TanStack Query, Tailwind 4): workflow list + builder with config forms *generated from the action JSON schemas*, trigger management, and a live execution view — engine events flow through the same `IListenFor<T>` seam into a SignalR hub (`/hubs/executions`) that invalidates queries in real time. Aspire starts the Vite dev server alongside the API (`npm install` in `src/web` once, then `aspire run`).

Previous (M2.5): Plugins can now *listen* to the engine, not just extend it: implement `IListenFor<TEvent>` for lifecycle events (`ExecutionStarted`, `StepCompleted`, `StepFailed`, `ExecutionCompleted`, `ExecutionFailed`). Events are best-effort and in-process — published after state is persisted, with per-listener fault isolation (a throwing listener is logged, never breaks an execution; encoded in tests). The whole engine composition lives in one shared `AddAutomateXEngine(...)` extension used by both `Program.cs` and the test fixture, so app/test config drift is impossible by construction. A `dotnet new automatex-plugin` template scaffolds new plugins.

Previous (M2): actions are plugin-contributed — implement `IAction<TConfig, TResult>` against `AutomateX.Plugin.Sdk`, decorate with `[Action]`, drop the published assembly in `plugins/` and it loads in an isolated, collectible `AssemblyLoadContext`. Config/result types are exported as JSON Schema via `GET /api/actions`. The built-in `http.request` is itself an SDK action.

Previous milestone (M1.5): Workflows live in Postgres as immutable versions; cron, webhook and manual triggers fire them; each step runs as its own durable Wolverine message with per-step retries + backoff (configurable via `Engine` options). Crash-resume comes from the durable inbox, the cron scheduler claims triggers with an atomic lease (no double-fires across nodes), trigger reschedules and outgoing messages commit atomically through the EF Core outbox, and a sweeper fails executions stuck past a threshold. Engine behavior is covered by integration tests against a real Postgres (Testcontainers).

## Stack

.NET 10 · Aspire 13 · Wolverine (Postgres-backed messaging) · EF Core 10 · FastEndpoints · Postgres

## Prerequisites

- .NET 10 SDK
- Docker (Aspire starts the Postgres container)
- Optional: [Aspire CLI](https://aspire.dev) for `aspire run`

## Run it

```bash
dotnet tool restore
aspire run   # or: dotnet run --project src/AutomateX.AppHost — or run AutomateX.AppHost from Rider
             # the web resource uses pnpm (WithPnpm installs packages on first start)
dotnet test  # engine integration tests (needs Docker — Testcontainers spins up Postgres)
```

The dashboard shows both resources: `api` (backend) and `web` (the SPA — open this one).

## Self-host

```bash
dotnet publish src/AutomateX -t:PublishContainer   # builds the automatex-api image
docker compose up -d
open http://localhost:8080                          # UI (8081 = direct API access)
```

Version tags (`v*`) publish images to GHCR via Actions — swap the compose `image:`/`build:` entries for `ghcr.io/eiromplays/automatex-api:latest` and `ghcr.io/eiromplays/automatex-web:latest` to skip local builds entirely.

- Plugins: drop `<Name>/<Name>.dll` into `./plugins` (volume-mounted) and restart the api — see `plugins/README.md`.
- Auth: set `Auth__ApiKey` in compose to gate `/api` + `/hubs`; enter the key via the ⚿ button in the UI header.
- Database: migrations apply on startup; the volume `automatex-postgres-data` holds state.

The M1.5 migration is expected to include Wolverine's envelope (inbox/outbox) tables — they're now mapped into the EF model so envelope writes commit atomically with entity saves. Easiest dev path: wipe the `automatex-postgres-data` volume first so EF owns the schema from scratch.

In development a `heartbeat` workflow (cron, every minute) is seeded automatically. Watch `GET /api/executions` fill up.

## API

```
POST   /api/workflows                       create (name, description, steps[])
PUT    /api/workflows/{id}                  update → creates a new immutable version
GET    /api/workflows · /api/workflows/{id}
POST   /api/workflows/{id}/execute          manual run → { executionId }
POST   /api/workflows/{workflowId}/triggers create trigger (type: cron|webhook, config)
DELETE /api/triggers/{id}
PUT    /api/connections/{id}                merge-update secrets (value=overwrite, null=delete)
POST   /api/webhooks/{triggerId}?secret=…   fire a webhook trigger → { executionId } (per-trigger secret, shown once at creation)
GET    /api/executions · /api/executions/{id}
GET    /api/actions                         action catalog with JSON config/result schemas
```

## Data flow between steps

Step configs are templates. `{{path}}` tokens resolve before each step runs:

```
{{trigger.payload}}            the JSON body sent to the webhook / manual execute call
{{trigger.payload.x.y}}        navigate it (object properties + array indices, camelCase)
{{steps.0.output.body}}        a prior step's output (0-based order)
{{connections.github.token}}   a field from an encrypted connection (see Connections page)
{{execution.id}}               {{workflow.id}}
```

A token that is the entire string keeps its JSON type (`"{{steps.0.output.statusCode}}"` → `200`,
not `"200"`); tokens inside longer strings interpolate. Unresolvable paths fail the step
immediately — no retries, the error tells you which segment broke.

Example — webhook payload `{"repo":"automatex"}` flowing through two steps:

```json
[
  { "actionType": "http.request",
    "config": { "method": "GET", "url": "https://api.github.com/repos/eiromplays/{{trigger.payload.repo}}" } },
  { "actionType": "sample.echo",
    "config": { "message": "Stars: {{steps.0.output.body}}" } }
]
```

## Writing a plugin

```csharp
public sealed record GreetConfig(string Name);
public sealed record GreetResult(string Greeting);

[Action("greet.hello", "Greet", Description = "Says hello.")]
public sealed class GreetAction : IAction<GreetConfig, GreetResult>
{
    public Task<GreetResult> ExecuteAsync(GreetConfig config, ActionContext context, CancellationToken ct = default)
        => Task.FromResult(new GreetResult($"Hello {config.Name}!"));
}
```

Plugins can also observe the engine via event listeners (constructor deps resolve from the host container):

```csharp
public sealed class MyListener(ILogger<MyListener> logger) : IListenFor<ExecutionFailed>
{
    public Task HandleAsync(ExecutionFailed e, CancellationToken ct = default)
    {
        logger.LogWarning("Workflow {WorkflowId} failed: execution {ExecutionId}", e.WorkflowId, e.ExecutionId);
        return Task.CompletedTask;
    }
}
```

Scaffold a new plugin: `dotnet new install ./templates/automatex-plugin`, then `dotnet new automatex-plugin -n MyPlugin`.

Project setup: reference `AutomateX.Plugin.Sdk` with `<Private>false</Private>` + `<ExcludeAssets>runtime</ExcludeAssets>`, set `<EnableDynamicLoading>true</EnableDynamicLoading>` (see `samples/AutomateX.SamplePlugin`). Deploy convention: `plugins/<PluginName>/<PluginName>.dll`, resolved relative to the app binary (override with `Engine:PluginsPath`).

Try the sample (echo + delay actions):

```bash
dotnet publish samples/AutomateX.SamplePlugin -o src/AutomateX/bin/Debug/net10.0/plugins/AutomateX.SamplePlugin
```

Restart, check `GET /api/actions`, then use `sample.echo` / `sample.delay` as workflow step types. `sample.delay` is also the crash/resume test tool — give it 30000ms, kill the app mid-step, restart, watch the durable inbox finish the job.

## Notes

- Package versions are pinned in `Directory.Packages.props` (CPM). `aspire update` keeps the Aspire packages and the `Aspire.AppHost.Sdk` version aligned.
- Wolverine runs with dev-time Roslyn codegen (`WolverineFx.RuntimeCompilation`). For the production image (M4), pre-generate with `dotnet run -- codegen write` and set `TypeLoadMode.Static` for faster cold starts.
- Wolverine is deliberately an experiment (see plan §4). If it doesn't earn its keep by M1, the fallback is plain DI handlers + a hand-rolled `FOR UPDATE SKIP LOCKED` worker.
- **v1 limitations, known and deliberate:** auth is an optional shared API key — fine on a trusted network, not for internet exposure; OIDC (Entra ID) is the next pass and the plan's original intent. Webhook payloads are ignored, actions have no idempotency keys, `ActionContext` carries services but not execution metadata (all land with input/output mapping). Plugin-contributed triggers and unload/reload still deferred. Engine events are best-effort in-process notifications.

## Milestones

M0 walking skeleton ✓ → M1 durable engine ✓ → M1.5 hardening ✓ → M2 plugin SDK ✓ → M2.5 platform polish ✓ → M3 UI ✓ → M4 ship (this). Details and definition-of-done in [the plan](docs/v2-plan.md).
