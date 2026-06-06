# AutomateX

Self-hostable, .NET-native automation engine. This is the v2 rewrite — architecture and scope live in [docs/v2-plan.md](docs/v2-plan.md). v1 is archived at [AutomateX-v1](https://github.com/Eiromplays/AutomateX-v1).

**Status: M2 — plugin SDK.** Actions are now plugin-contributed: implement `IAction<TConfig, TResult>` against `AutomateX.Plugin.Sdk`, decorate with `[Action]`, drop the published assembly in the `plugins/` folder and the engine loads it in an isolated, collectible `AssemblyLoadContext` at startup. Config and result types are exported as JSON Schema via `GET /api/actions` (the future UI generates forms from these). The built-in `http.request` is itself an SDK action.

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
dotnet dotnet-ef migrations add WolverineOutbox --project src/AutomateX   # after pulling M1.5
aspire run   # or: dotnet run --project src/AutomateX.AppHost — or run AutomateX.AppHost from Rider
dotnet test  # engine integration tests (needs Docker — Testcontainers spins up Postgres)
```

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
POST   /api/webhooks/{triggerId}            fire a webhook trigger → { executionId }
GET    /api/executions · /api/executions/{id}
GET    /api/actions                         action catalog with JSON config/result schemas
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
- Remaining known seams: webhook payloads are ignored, actions have no idempotency keys, and `ActionContext` carries services but not yet execution metadata — all land with input/output mapping. Deferred from M2 to M2.5: plugin event listeners (`IListenFor<T>`), plugin-contributed trigger types, the `dotnet new automatex-plugin` template, and plugin unload/reload (the load contexts are already collectible).

## Milestones

M0 walking skeleton ✓ → M1 durable engine ✓ → M1.5 hardening ✓ → M2 plugin SDK (this) → M3 UI → M4 ship. Details and definition-of-done in [the plan](docs/v2-plan.md).
