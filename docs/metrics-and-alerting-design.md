# Metrics + failure alerting (v3.5)

Two independent concerns that share one existing seam: the engine events already published on
every settle path — `ExecutionStarted`, `StepCompleted`, `StepFailed`, `ExecutionCompleted`,
`ExecutionFailed` (`AutomateX.Plugin.Sdk/Events.cs`) — consumed via `IListenFor<T>`. Those
listeners are **best-effort, in-process, post-commit**: a throw is logged and skipped, never part
of the transactional flow, so it can't affect an execution's outcome (the `SignalRExecutionEventListener`
is the precedent). Both features below are pure additive listeners + config. **No engine or
state-machine changes.**

---

## Part A — Metrics (OTel + Prometheus)

**Today:** Aspire `ServiceDefaults` wires OTel with AspNetCore/HttpClient/Runtime instrumentation and
an **OTLP exporter gated on `OTEL_EXPORTER_OTLP_ENDPOINT`**. There are no domain metrics and no
Prometheus scrape endpoint. This milestone finishes both.

**Meter** `"AutomateX"` (static, in the engine assembly), instruments — all low-cardinality:

| Instrument | Kind | Tags |
| --- | --- | --- |
| `automatex.executions.started` | Counter\<long> | `trigger` |
| `automatex.executions.settled` | Counter\<long> | `status` (Succeeded\|Failed) |
| `automatex.execution.duration` | Histogram\<double> (s) | `status` |
| `automatex.steps.settled` | Counter\<long> | `action`, `status` (Succeeded\|Failed) |

(Step `status` is `Succeeded`/`Failed` only — the event stream has no distinct "caught" signal, so a
step caught by an error edge counts as `Failed` at the action level.)

**Cardinality discipline:** tags are bounded enums/action-types only. **No `workflowId`/`executionId`
labels** (unbounded → Prometheus cardinality blowup). Per-workflow health stays in the in-app
dashboard via `StatsCalculator` (DB-derived snapshot); OTel is the time-series for external
monitoring. Different consumers, no overlap.

**Feed:** a `MetricsEventListener : IListenFor<ExecutionStarted | StepCompleted | StepFailed |
ExecutionCompleted | ExecutionFailed>` records to the instruments. The `*Completed`/`*Failed`
execution events carry only ids (SDK contract — **don't break it**), so for `execution.duration`
the listener does one indexed PK lookup of `StartedAt`/`CompletedAt`. Off the hot path (post-commit,
best-effort), so the read is fine.

**Wiring:**
- `metrics.AddMeter("AutomateX")` in `ServiceDefaults.ConfigureOpenTelemetry`.
- Add `OpenTelemetry.Exporter.Prometheus.AspNetCore`; `.AddPrometheusExporter()`;
  `app.MapPrometheusScrapingEndpoint()` → `/metrics`.
- Gate the scrape endpoint behind `Metrics__EnablePrometheus` (default **on**). The OTLP push path is
  unchanged (still gated on the env var); pull + push coexist.
- `/metrics` sits **outside** the API-key gate (like `/health`) so a scraper on the private network
  can read it. It exposes aggregate counters only — no payloads, no secrets — but operators behind a
  public ingress should ACL it (documented in the recipe + security posture).

**Testing:** capture measurements with a `MeterListener` in a test — a succeeded run records
`executions.settled{status=Succeeded}=1` + a duration sample; a failed run records `status=Failed`;
a step records `steps.settled{action,status}`. Keep it light.

---

## Part B — Failure alerting (`execution.onFailure` trigger)

**Design choice:** "everything is a workflow." Add an **engine-native trigger type**
`execution.onFailure`, mirroring the existing `"workflow"` chaining trigger (also engine-native,
matched at a terminal site, dispatches `RunWorkflow` with a built payload — see `WorkflowChaining`).
Any workflow subscribes by adding this trigger; when an execution fails, the engine starts each
subscriber with a failure summary as `{{trigger.payload}}`. The alert workflow then does whatever —
`matrix.send`, `slack.send`, `webhook.send`, open a ticket.

Why a trigger and not a config setting: authored in the builder (no new settings surface),
filterable, supports multiple subscribers, fully dogfoodable, reuses the whole engine + send
connectors. Symmetric with workflow chaining.

**Seam:** `FailureAlertListener : IListenFor<ExecutionFailed>`. On failure:
1. Load the failed execution (+ earliest failed step, workflow name).
2. Find **enabled** triggers of `Type == "execution.onFailure"` in the **same workspace**; optional
   config `watchWorkflowId` scopes to one source workflow (default: all in the workspace).
3. For each, `PublishAsync(new RunWorkflow(newId, trigger.WorkflowId, "execution.onFailure", payload,
   trigger.EntryStepOrder))`.

**Payload** (`{{trigger.payload}}`):

```json
{
  "executionId": "…",
  "workflowId": "…",
  "workflowName": "…",
  "status": "Failed",
  "failedStep": { "order": 2, "key": "deploy", "actionType": "ssh.command", "error": "…" },
  "startedAt": "…",
  "completedAt": "…",
  "url": "{base}/executions/{id}"   // when Engine__PublicBaseUrl is set, else null
}
```

**Loop guards (the real failure mode is an alert storm — over-test these):**
- **Self-exclusion:** never alert for an execution whose `TriggeredBy == "execution.onFailure"`. The
  alert workflow's own failure must not re-trigger alerts. One string check; `TriggeredBy` already exists.
- **Top-level only (default):** skip `Depth > 0` executions (sub-workflow / `forEach` children) —
  their failure already surfaces on the parent, which fails and alerts once. Config
  `includeSubWorkflows` (default false) opts in.
- **Fire-once:** `ExecutionFailed` already publishes exactly once per execution (settle guard) — no
  extra dedup needed.
- Belt-and-suspenders: these dispatch through `RunWorkflow`, so the existing `MaxChainDepth` cap also
  bounds any pathological chain.

**Builder:** add `execution.onFailure` to the trigger-type picker (alongside cron/webhook/manual/
workflow/plugin), with an optional "only this workflow" select + "include sub-workflows" toggle.
Import/export: portable like `cron` (include it; it's not webhook/workflow-special).

**Tests-first:**
- Failing workflow + a second workflow subscribed via `execution.onFailure` → the subscriber runs and
  its `{{trigger.payload}}` carries the failed id / workflowName / `failedStep.error`.
- The subscriber's own failure does **not** spawn another alert (self-exclusion).
- A sub-workflow child failure does **not** alert (Depth>0 suppressed); the parent's failure does.
- `watchWorkflowId` filter: only matching-source failures alert.

---

## Scope / sequencing (each its own commit under a v3.5 sub-tag)

1. **This design note.**
2. **Metrics** — `Meter` + instruments + `MetricsEventListener` + OTel/Prometheus wiring (+ `MeterListener` test).
3. **Failure alerting** — `execution.onFailure` trigger type + `FailureAlertListener` + payload + guards (tests-first).
4. **Builder** — author `execution.onFailure` (+ filter/toggle) + import/export.
5. **Wrap-up** — CHANGELOG + recipe (failure alerting) + release notes.

## Risks

- **Metric cardinality** — enforce bounded tags (no ids). Reviewed in PR.
- **`/metrics` exposure** — aggregate-only, outside the API-key gate; behind a config flag; document
  ACL for public ingress.
- **Alert loops** — self-exclusion (`TriggeredBy`) + `Depth>0` suppression + fire-once settle; over-tested.
- **Listener latency** — best-effort post-commit; a throw can't affect executions (existing contract),
  but keep DB reads to indexed PK lookups so a slow listener can't back up the in-proc bus.
