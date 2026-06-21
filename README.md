# AutomateX

[![CI](https://github.com/Eiromplays/AutomateX/actions/workflows/ci.yml/badge.svg)](https://github.com/Eiromplays/AutomateX/actions/workflows/ci.yml)

Self-hostable, .NET-native automation engine. Build workflows — sequences (and branches) of **steps**
fired by **triggers** — in a visual builder, run them on a durable engine that survives restarts, and
extend everything with plugins. Think Zapier/n8n, but yours: your hardware, your data, your code.

Release history is in [CHANGELOG.md](CHANGELOG.md). v1 is archived at
[AutomateX-v1](https://github.com/Eiromplays/AutomateX-v1).

## Screenshots

The visual builder — `switch` routing into parallel lanes that rejoin, with continue-on-failure:

![Workflow builder with branching canvas](docs/img/builder-branching.png)

The dashboard (execution metrics + per-workflow health) and a live execution run graph:

![Dashboard](docs/img/dashboard.png)
![Execution run graph](docs/img/execution-graph.png)

Every action renders a form from its JSON Schema, with inline `{{connections.…}}` autocomplete on
config fields:

![Form editor with connection autocomplete](docs/img/form-editor.png)

## Highlights

- **Durable engine** — each step is a Postgres-backed Wolverine message with per-step retries and
  backoff; crashes resume from the durable inbox, cron fires via an atomic lease (no double-fires).
- **Visual builder** — graph + forms generated from each action's JSON Schema, with connection-ref
  validation, required-field hints, and inline `{{connections.…}}` autocomplete.
- **Branching & parallel** — `switch`/`gate` routing over an edge-DAG, parallel fan-out lanes that
  join, and continue-on-failure.
- **Triggers** — cron, webhook (per-trigger capability secrets), manual, workflow-chaining, and
  plugin triggers (`rss`, `http.poll`, `matrix.onMessage`).
- **Actions** — built-in `http.request`, `gate`, `switch`, `kv.*`, `schedule.workflow`, `llm.prompt`,
  `llm.agent`, `mcp.call`; first-party plugins `ssh.command`, `matrix.send`, `discord.send`,
  `slack.send`, `telegram.send`, `pushover.send`, `email.send`.
- **Durable KV store** — per-workflow state via `kv.*`; `setIfAbsent` + `gate` gives run-once dedup
  ([recipe](docs/recipes/dedup-and-state.md)).
- **Encrypted connections** — AES-256-GCM secret bundles + OAuth2 connections, referenced as
  `{{connections.<name>.<field>}}`, masked everywhere.
- **Workspaces & auth** — viewer/editor/owner roles; auth is open → API key → OIDC (with
  refresh-token sessions).
- **Plugin platform** — plugins contribute actions, triggers, and connection types; hot-reload,
  workspace-scoped plugins, and an in-app catalog with sha256-verified installs (upload gated behind
  `Engine__AllowPluginUpload`).
- **Self-hosting** — `docker compose up`, GHCR images on `v*` tags, and a full homelab guide
  (Proxmox + Tailscale HTTPS + OIDC) in [docs/deploy-homelab.md](docs/deploy-homelab.md).

## Stack

.NET 10 · Aspire 13 · Wolverine (Postgres-backed messaging) · EF Core 10 · FastEndpoints · Postgres ·
React Router v7 / React 19 / TanStack Query / Tailwind 4

## Run it

Prerequisites: **.NET 10 SDK**, **Docker** (Aspire starts Postgres), **Node + pnpm**, optionally the
[Aspire CLI](https://aspire.dev).

```bash
dotnet tool restore
aspire run    # api + web (Vite) + Postgres — open the "web" resource
dotnet test   # engine + module tests (needs Docker via Testcontainers)
```

Web app checks (in `src/web`): `pnpm test && pnpm typecheck`.

## Self-host

```bash
dotnet publish src/AutomateX -t:PublishContainer   # builds the automatex-api image
docker compose up -d
open http://localhost:8080                          # UI (8081 = direct API)
```

`v*` tags publish images to GHCR — point the compose `image:` entries at
`ghcr.io/eiromplays/automatex-api:latest` / `automatex-web:latest` to skip local builds. Running 24/7
on a server? Use `docker-compose.prod.yml` + `.env.example`; the full walkthrough (Proxmox, Tailscale
Serve HTTPS, OIDC, updates, backups) is in [docs/deploy-homelab.md](docs/deploy-homelab.md).

- **Plugins**: drop `<Name>/<Name>.dll` into the volume-mounted `./plugins` and restart the api, or
  install from the in-app catalog. See `plugins/README.md`.
- **Auth**: set `Auth__ApiKey` (or OIDC) to gate `/api` + `/hubs`.
- **Encryption**: `Encryption__Key` decrypts connection secrets and is never stored — back it up.
- **Database**: migrations apply on startup; the `automatex-postgres-data` volume holds state.

## Data flow between steps

Step configs are templates. `{{path}}` tokens resolve before each step runs:

```
{{trigger.payload}}            the JSON body sent to the webhook / manual execute call
{{trigger.payload.x.y}}        navigate it (object properties + array indices, camelCase)
{{steps.0.output.body}}        a prior step's output (0-based order)
{{connections.github.token}}   a field from an encrypted connection
{{execution.id}}               {{workflow.id}}
```

A token that is the entire string keeps its JSON type (`"{{steps.0.output.statusCode}}"` → `200`,
not `"200"`); tokens inside longer strings interpolate. Unresolvable paths fail the step immediately
— no retries, the error names the segment that broke.

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

Plugins implement `IAction<TConfig, TResult>` (actions), `ITriggerListener<TConfig>` (triggers), or
`IConnectionType` (guided connection types — with `IOAuthConnectionType` for OAuth2 and
`IConnectionTester` for a "Test" button) against `AutomateX.Plugin.Sdk`; config/result types export
as JSON Schema and drive the builder forms. Scaffold one:

```bash
dotnet new install ./templates/automatex-plugin
dotnet new automatex-plugin -n MyPlugin
```

First-party plugins live under `src/Plugins`; the sample (echo/delay actions) is in
`samples/AutomateX.SamplePlugin`. Deploy convention: `plugins/<PluginName>/<PluginName>.dll`
(override the root with `Engine__PluginsPath`).

## Docs

- Deployment: [homelab guide](docs/deploy-homelab.md)
- Recipes: [self-deploy](docs/recipes/self-deploy.md) ·
  [dedup & durable state](docs/recipes/dedup-and-state.md) ·
  [conditional gate](docs/recipes/conditional-gate.md) · [reminders](docs/recipes/reminders.md) ·
  [jarvis-lite](docs/recipes/jarvis-lite.md) · [backups](docs/recipes/backups.md)
- Design notes: [branching](docs/branching-design.md) ·
  [trigger → lane routing](docs/trigger-lane-routing-design.md) ·
  [OAuth connections](docs/oauth-connections-design.md) · [llm.agent](docs/llm-agent-design.md) ·
  [mcp.call](docs/mcp-call-design.md)

## Contributing & security

See [CONTRIBUTING.md](CONTRIBUTING.md) for setup and conventions, and [SECURITY.md](SECURITY.md) to
report a vulnerability privately. Licensed under [LICENSE](LICENSE).
