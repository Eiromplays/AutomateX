# AutomateX

Self-hostable, .NET-native automation engine. This is the v2 rewrite — architecture and scope live in [docs/v2-plan.md](docs/v2-plan.md). v1 is archived at [AutomateX-v1](https://github.com/Eiromplays/AutomateX-v1).

**Status: M1 — durable engine.** Workflows live in Postgres as immutable versions; cron, webhook and manual triggers fire them; each step runs as its own durable Wolverine message with per-step retries + backoff. Crash-resume comes from the durable inbox — in-flight step messages are redelivered and the handlers are idempotent on redelivery.

## Stack

.NET 10 · Aspire 13 · Wolverine (Postgres-backed messaging) · EF Core 10 · FastEndpoints · Postgres

## Prerequisites

- .NET 10 SDK
- Docker (Aspire starts the Postgres container)
- Optional: [Aspire CLI](https://aspire.dev) for `aspire run`

## Run it

```bash
dotnet tool restore
dotnet dotnet-ef migrations add AddWorkflowsAndTriggers --project src/AutomateX   # after pulling M1
aspire run   # or: dotnet run --project src/AutomateX.AppHost — or run AutomateX.AppHost from Rider
```

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
```

## Notes

- Package versions are pinned in `Directory.Packages.props` (CPM). `aspire update` keeps the Aspire packages and the `Aspire.AppHost.Sdk` version aligned.
- Wolverine runs with dev-time Roslyn codegen (`WolverineFx.RuntimeCompilation`). For the production image (M4), pre-generate with `dotnet run -- codegen write` and set `TypeLoadMode.Static` for faster cold starts.
- Wolverine is deliberately an experiment (see plan §4). If it doesn't earn its keep by M1, the fallback is plain DI handlers + a hand-rolled `FOR UPDATE SKIP LOCKED` worker.
- Known M1 seams (deliberate): no transactional outbox between DB saves and message publishes yet (rare duplicate-fire/missed-cascade windows — candidate for Wolverine's EF Core outbox integration), webhook payloads are ignored, and actions have no idempotency keys. Engine tests are the other M1 leftover.

## Milestones

M0 walking skeleton ✓ → M1 durable engine (this) → M2 plugin SDK → M3 UI → M4 ship. Details and definition-of-done in [the plan](docs/v2-plan.md).
