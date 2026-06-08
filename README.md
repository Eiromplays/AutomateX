# AutomateX

[![CI](https://github.com/Eiromplays/AutomateX/actions/workflows/ci.yml/badge.svg)](https://github.com/Eiromplays/AutomateX/actions/workflows/ci.yml)

Self-hostable, .NET-native automation engine. This is the v2 rewrite — architecture and scope live in [docs/v2-plan.md](docs/v2-plan.md). v1 is archived at [AutomateX-v1](https://github.com/Eiromplays/AutomateX-v1).

**Status: v2.9 — convergence (in progress).** The big pre-v3 release. Landed so far: `matrix.onMessage` (talk to your homelab) and **scheduled workflows** — a built-in `schedule.workflow` action exposing the engine's durable scheduler, so any workflow can queue a future run of another (`delaySeconds`/`runAt` + payload, one-shot, workspace-bound). Composed, they make **natural-language reminders** real ([docs/recipes/reminders.md](docs/recipes/reminders.md)): *"remind me in 2 hours"* → Matrix → local LLM parses it → durable scheduled delivery. Connection types (guided third-party setup) and **SignalR workspace groups** (live events now route per-workspace — a client only sees its own workspace's events and patches the list in place instead of refetching) are in. Plus plugins-page tabs, editable triggers, and **conditionals** — a `gate` built-in that stops a workflow unless a condition holds (a closed gate skips later steps; the execution still succeeds), the deterministic "check-and-act-if" primitive ([docs/recipes/conditional-gate.md](docs/recipes/conditional-gate.md)). And **refresh-token sessions**: OIDC now requests `offline_access` and saves tokens in the auth cookie, so `OnValidatePrincipal` silently refreshes the access token at the provider just before it expires — the session's liveness now tracks the IdP (a revoked or disabled user is signed out at the next refresh boundary) instead of sliding blindly for 8 hours. That closes the v2.9 convergence set.

Previous (v2.9 start): **the ears.** `matrix.onMessage`: a sync long-polling trigger that fires workflows when Matrix messages arrive — own messages never trigger (unconditional loop protection), pre-start history is skipped, optional room filter. With it, the enabler: **trigger configs resolve `{{connections.<name>.<field>}}` at listener start**, so secret-bearing triggers store templates, never tokens. The composed payoff is [docs/recipes/jarvis-lite.md](docs/recipes/jarvis-lite.md): *message → local LLM → reply* — a conversational assistant on your own hardware, with execution history as the chat's audit trail.

Previous (v2.8): **the plugin platform, finished.** Plugins can now contribute **trigger types**: implement `ITriggerListener<TConfig>` + `[Trigger]`, call `context.FireAsync(payload)` from your long-running listener (return = poll cycle, throw = restart with backoff), and the engine supervises one listener per enabled trigger row — config edits and hot-reloads restart them cleanly. Trigger config schemas render as real forms in the UI (`sample.ticker` is the demo). And the **plugin catalog**: releases publish every first-party plugin as zip assets plus a `catalog.json`; the Actions page lists them with one-click **Install** — downloaded, **sha256-verified before touching disk**, extracted and hot-reloaded, behind the same `Engine__AllowPluginUpload` gate. Trigger types come from global plugins only, same rule as event listeners.

Previous (v2.7): **llm.prompt, export/import, retry.** OpenAI-compatible LLM action (optional apiKey for local endpoints). Portable workflow documents — secrets excluded by construction, review-before-create import through the builder. Execution retry: byte-identical payload replay on the latest version, lineage everywhere (execution trees, retry/chain banners).

Previous (v2.6): **workflow chaining.** Workflows compose: a **`workflow` trigger** ("when workflow X succeeds / fails / finishes") fires its workflow with the source execution as payload — `{{trigger.payload.source.executionId}}`, `…source.status`, and the source's own `…source.triggerPayload` propagate context down the chain. Chained dispatches ride the same durable outbox as step cascades (crash-safe, not best-effort), never cross workspaces, and are capped by `Engine__MaxChainDepth` (default 5) so loops strangle quietly instead of melting the instance. The trigger UI grew a chain builder ("when *workflow* succeeds") and chain summaries (⛓) on the workflow page. v1's workflow stacking, rebuilt on v2's spine.

Previous (v2.5): **guardrails & receipts** — in-use delete guards for plugins *and* connections (latest-version scan, named blockers, `force=true` override), schema-drift warnings (config keys the active plugin version doesn't define), app-wide toasts with clean error envelopes, plugin build fingerprints, and a self-deploy trigger step in the release workflow (inert until the `AUTOMATEX_DEPLOY_WEBHOOK` secret exists).

Previous (v2.4): **live plugin platform.** Plugins **hot-reload**: `POST /api/actions/reload` (or the button on the Actions page) re-scans the plugin folders and atomically swaps the assemblies, action registry and event subscriptions — in-flight executions finish on the code they started with (their old `AssemblyLoadContext` unloads once they drain), new executions pick up the new code. Loads are **shadow-copied** to unique temp paths: the runtime's PE-image cache is path-keyed, so without the copy a replaced DLL reloads stale (and Windows would lock the file); the plugin manager shows each plugin's build fingerprint (MVID) so "what code is loaded" is always observable. Plugins can be **workspace-scoped**: dropped in (or uploaded to) `plugins/.workspaces/<workspace-id>/`, visible and runnable only in that workspace, shadowing global actions on name collisions — workspace plugins contribute actions only, never event listeners (engine events are instance-wide). **Upload** (zip of the flat `dotnet publish` output, named `<PluginName>.zip`) is gated behind `Engine__AllowPluginUpload=true`, default off — an uploaded plugin is code running with full host trust; workspace uploads additionally require that workspace's Owner. The Actions page grew a plugin manager (upload/delete per scope) and `core`/`plugin`/`workspace` provenance chips. Also in this release: **`http.request` v2** — `headers` (templated, so `{{connections.<name>.token}}` authenticates against private APIs and is masked in outputs), `contentType` (bodies now default to `application/json` — behavior change from text/plain), `timeoutSeconds`, opt-in `failOnErrorStatus` (non-2xx fails the step and earns retries), and response headers in the result (`{{steps.0.output.headers.x-request-id}}`); plus `matrix.send` gains `msgType` (`m.text`/`m.notice`).

Previous (v2.3): **workflow lifecycle.** Workflows are now editable and deletable from the UI: editing **appends an immutable version** (`PUT /api/workflows/{id}` existed since M1 — the UI finally caught up), so past executions keep the exact version and outputs they ran with, while new runs pick up the latest — version pinning is encoded in engine tests. Deleting a workflow removes its versions, steps, triggers and execution history atomically; finished executions can be deleted individually (running ones refuse — their inbox messages would write to ghosts). Step lists everywhere show provenance badges (`core`/`plugin`, hover for the plugin name). Past versions are listed on the workflow page and can be **restored** — git-revert style: restoring vN appends a new version with vN's steps copied, never rewriting history (restoring the current version is rejected).

Previous (v2.2): **the platform deploys itself.** Two first-party plugins under `src/Plugins`: **`ssh.command`** (SSH.NET; password or private-key auth fed from `{{connections.…}}`, optional SHA-256 host-key pinning, captures exit code/stdout/stderr, non-zero exit fails the step) and **`matrix.send`** (Matrix room messages with transaction ids deterministic per execution step, so engine retries can't double-send — the homeserver dedupes). Together they close the loop v1 was loved for: GitHub release → webhook trigger → detached `docker compose pull && up -d` over a forced-command SSH key → Matrix announcement, end-to-end in [docs/recipes/self-deploy.md](docs/recipes/self-deploy.md). SSH behavior is integration-tested against a throwaway `testcontainers/sshd` container, same posture as the engine's Postgres.

Previous (v2.1): Workflows, connections and executions now live in **workspaces**: separate spaces with invitable members and viewer/editor/owner roles — authentication became authorization. Members are invited by email (access starts on first sign-in); a workspace with zero members is open to every signed-in user and is *claimed* by adding its first member; the last Owner can never be removed. Requests scope via the `X-Workspace-Id` header (absent = the Default workspace, so existing clients keep working); connection resolution is workspace-isolated in the engine itself; SignalR broadcasts carry only execution ids (details refetch through the authorized API). In open/API-key modes workspaces degrade gracefully to folders. The `Default` workspace adopts all pre-workspace data and cannot be deleted.

Previous (v2.0): Auth is now a tri-state: **open** (nothing configured, local default) → **API key** (`Auth__ApiKey`; `X-Api-Key` for scripts, ⚿ cookie exchange in the UI) → **OIDC** (`Auth__Authority` + `Auth__ClientId` + `Auth__ClientSecret`): the API owns the code flow via standard ASP.NET middleware (`/auth/login`, `/signin-oidc`), the browser holds only an HttpOnly auth cookie — no tokens in JS, no BFF needed thanks to the same-origin proxy. When OIDC is on, the UI gates behind a sign-in screen and the API key keeps working for machine clients. Entra setup: app registration (Web platform), redirect URI `https://<host>/signin-oidc` (+ `http://localhost:5180/signin-oidc` for dev), Authority `https://login.microsoftonline.com/<tenant-id>/v2.0`.

Previous (v1.3): Connections gain `PUT /api/connections/{id}` with merge semantics (filled = overwrite, removed = delete, untouched = keep — rotate one field without re-entering the rest; names immutable) and an edit form in the UI. Webhook triggers get **per-trigger secrets**: generated server-side, shown exactly once at creation (`/api/webhooks/{id}?secret=…` or `X-Webhook-Secret`), validated fixed-time — and `/api/webhooks` moves *outside* the global API-key gate, so third-party senders never hold the instance key. Webhook triggers created before v1.3 must be recreated to obtain a secret.

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
GET    /api/workspaces · POST /api/workspaces · DELETE /api/workspaces/{id}
GET/POST /api/workspaces/{id}/members · DELETE /api/workspaces/{id}/members/{memberId}
```

Data endpoints scope to the workspace in the `X-Workspace-Id` header (absent = Default).

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

First-party plugins ship in-repo under `src/Plugins` — `ssh.command` (remote commands with key/password auth and host-key pinning) and `matrix.send` (deduplicated Matrix notifications). Publish them the same way; together they power the [self-deploy recipe](docs/recipes/self-deploy.md):

```bash
dotnet publish src/Plugins/AutomateX.Plugins.Ssh    -o src/AutomateX/bin/Debug/net10.0/plugins/AutomateX.Plugins.Ssh
dotnet publish src/Plugins/AutomateX.Plugins.Matrix -o src/AutomateX/bin/Debug/net10.0/plugins/AutomateX.Plugins.Matrix
dotnet publish src/Plugins/AutomateX.Plugins.Llm    -o src/AutomateX/bin/Debug/net10.0/plugins/AutomateX.Plugins.Llm
```

`llm.prompt` (in `AutomateX.Plugins.Llm`) talks to any OpenAI-compatible chat-completions endpoint — OpenAI, OpenRouter, Ollama, LM Studio — via `baseUrl`; `apiKey` is optional so local models work out of the box, and the completion lands in `{{steps.<n>.output.text}}` for the next step.

Restart, check `GET /api/actions`, then use `sample.echo` / `sample.delay` as workflow step types. `sample.delay` is also the crash/resume test tool — give it 30000ms, kill the app mid-step, restart, watch the durable inbox finish the job.

## Notes

- Package versions are pinned in `Directory.Packages.props` (CPM). `aspire update` keeps the Aspire packages and the `Aspire.AppHost.Sdk` version aligned.
- Wolverine runs with dev-time Roslyn codegen (`WolverineFx.RuntimeCompilation`). For the production image (M4), pre-generate with `dotnet run -- codegen write` and set `TypeLoadMode.Static` for faster cold starts.
- Wolverine is deliberately an experiment (see plan §4). If it doesn't earn its keep by M1, the fallback is plain DI handlers + a hand-rolled `FOR UPDATE SKIP LOCKED` worker.
- **v1 limitations, known and deliberate:** auth is an optional shared API key — fine on a trusted network, not for internet exposure; OIDC (Entra ID) is the next pass and the plan's original intent. Webhook payloads are ignored, actions have no idempotency keys, `ActionContext` carries services but not execution metadata (all land with input/output mapping). Plugin-contributed triggers and unload/reload still deferred. Engine events are best-effort in-process notifications.

## Milestones

M0 walking skeleton ✓ → M1 durable engine ✓ → M1.5 hardening ✓ → M2 plugin SDK ✓ → M2.5 platform polish ✓ → M3 UI ✓ → M4 ship (this). Details and definition-of-done in [the plan](docs/v2-plan.md).
