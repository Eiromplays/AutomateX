# AutomateX v3.5.0

Operability: AutomateX now tells you when something breaks, and exposes the numbers.

## Highlights

- **Failure alerting — the `execution.onFailure` trigger.** Build one alert workflow, give it an
  `execution.onFailure` trigger, and every failed execution in the workspace runs it with a failure
  summary as `{{trigger.payload}}` (`workflowName`, `failedStep.{key,actionType,error}`, and a `url`
  when `Engine__PublicBaseUrl` is set). Notify however you like — `matrix.send`, `slack.send`,
  `webhook.send`, open a ticket. It's collected on the durable terminal path, so alerts ride the
  outbox and survive restarts. Loop-guarded: an alert run never re-alerts (self-exclusion), and
  sub-workflow/`forEach` children are suppressed unless you opt in. Scope it to one source workflow
  with `watchWorkflowId`, or leave it workspace-wide. Authored in the builder; exports portably.
- **Metrics — OpenTelemetry + Prometheus.** Domain instruments (`automatex.executions.started`,
  `automatex.executions.settled`, `automatex.execution.duration`, `automatex.steps.settled`) with
  bounded `status`/`action`/`trigger` tags. Scrape `GET /metrics` (Prometheus pull, on by default,
  outside the API-key gate) or push via OTLP (`OTEL_EXPORTER_OTLP_ENDPOINT`) — both at once is fine.

The two pair up: the metric tells you the failure rate moved; the alert tells you which run and which
step. See the [failure-alerting recipe](docs/recipes/failure-alerting.md).

## Upgrade notes

- **No migration.** `execution.onFailure` is an engine-native trigger (no schema change); metrics add
  no tables.
- **`/metrics` is on by default** and unauthenticated (aggregate counters only — no payloads or
  secrets, same posture as `/health`). Behind a public ingress, ACL it or set
  `Metrics__EnablePrometheus=false`.

Full history: [CHANGELOG.md](CHANGELOG.md).

---

*Next on the v3.5 ops/hardening track: action idempotency keys, audit log + instance-admin role,
per-tenant DEKs, and out-of-proc plugin sandboxing.*
