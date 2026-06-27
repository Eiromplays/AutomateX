# Recipe: failure alerting & metrics

Get told when something breaks — and scrape the numbers — without wiring it into every workflow.

## Alert me when anything fails (`execution.onFailure`)

Build one **alert workflow** and give it an `execution.onFailure` trigger. When any execution in the
workspace settles `Failed`, AutomateX starts your alert workflow with a summary of the failure as
`{{trigger.payload}}`:

```
execution.onFailure              (trigger — "On failure", scope: any workflow)
matrix.send   "notify"           message = ⚠ {{trigger.payload.workflowName}} failed at
                                           {{trigger.payload.failedStep.key}}:
                                           {{trigger.payload.failedStep.error}}
                                           {{trigger.payload.url}}
```

That's it — every failure in the workspace now pings the room. Swap `matrix.send` for `slack.send`,
`telegram.send`, `webhook.send`, or a multi-step flow (open a ticket, page on-call, etc.).

### The payload

```json
{
  "executionId": "…",
  "workflowId": "…",
  "workflowName": "nightly-backup",
  "status": "Failed",
  "failedStep": { "order": 2, "key": "upload", "actionType": "ssh.command", "error": "exit 1" },
  "startedAt": "…",
  "completedAt": "…",
  "url": "https://automatex.example.com/executions/…"
}
```

`url` is filled only when `Engine__PublicBaseUrl` is set; `failedStep` is the earliest failed step.

### Scoping & options

- **Scope** — leave it on *any workflow* (workspace-wide) or pick one source workflow (`watchWorkflowId`)
  to alert only on that workflow's failures.
- **Sub-workflows** — child failures (`workflow.call` / `forEach`) are **suppressed by default**: a
  failed child surfaces on its parent, which fails and alerts once. Tick *include sub-workflow /
  forEach failures* if you want an alert per child too.

### Loop safety

The alert workflow can use any actions, including ones that might fail — its own failure never spawns
another alert (runs triggered by `execution.onFailure` are self-excluded). Failures fire the alert
exactly once, and everything rides the durable outbox, so an alert can't be lost to a restart.

### Portability

`execution.onFailure` triggers export with the workflow. The instance-local `watchWorkflowId` is
dropped on export (the trigger imports as workspace-wide) — re-scope it after import if you need to.

## Metrics (Prometheus / OpenTelemetry)

AutomateX emits domain metrics on the `AutomateX` meter:

| Instrument | Tags |
| --- | --- |
| `automatex.executions.started` | `trigger` |
| `automatex.executions.settled` | `status` |
| `automatex.execution.duration` (seconds) | `status` |
| `automatex.steps.settled` | `action`, `status` |

Two ways out, usable together:

- **Prometheus pull** — scrape `GET /metrics`. On by default; disable with `Metrics__EnablePrometheus=false`.
  It sits outside the API-key gate (aggregate counters only — no payloads or secrets), so a scraper on
  your private network just works. **ACL it** if the instance is behind a public ingress.
- **OTLP push** — set `OTEL_EXPORTER_OTLP_ENDPOINT` to ship metrics (and traces) to any OTLP collector.

For the in-app view (per-workflow health, recent failures), the dashboard's `GET /api/stats` is the
DB-derived snapshot; the meter above is the time series for Grafana/Prometheus.
