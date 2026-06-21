# AutomateX v3.x roadmap (v3.2 → v3.5)

**Status:** planning. Sequences the four themes chosen after v3.1 into point releases. Order is by
dependency and leverage, not preference — each theme is wanted; this is how they stack.

## TL;DR

v3.1 shipped named step references, `transform`/`webhook.send`, Slack/Telegram, and React Router 8.
Next, in order:

1. **v3.2 — Control flow & resilience.** Try/catch error branches, durable wait / human approval,
   retry-from-a-step. Turns "react to an event" into "run a process." Builds on the edge-DAG router
   and the durable engine.
2. **v3.3 — Loops & sub-workflows.** Call a workflow as a step, then `forEach`/map a sub-flow over an
   array. Closes the biggest expressiveness gap; reuses v3.2's suspend/resume primitive.
3. **v3.4 — Connector & trigger breadth.** DB query, GitHub, Google, IMAP-received, S3. Independent
   plugins, parallelizable, low cross-risk.
4. **v3.5 — Ops & hardening.** Out-of-proc plugin sandboxing, audit log + instance-admin, metrics +
   failure alerting, idempotency keys, per-tenant DEKs. The "exposed/multi-tenant" tier.

Keystone: the **durable wait** primitive in v3.2 is what v3.3's synchronous sub-workflow call reuses.
Build it well.

---

## v3.2 — Control flow & resilience

> **Shipped in v3.2.0:** try/catch error branches (below). **Durable wait / human approval** and
> **retry-from-a-step** roll into v3.3 — they share the suspend/resume keystone, so they sequence
> together.


The deterministic core has `gate` (linear halt) and `switch` (conditional fork). What's missing is
**failure as a routable outcome** and **time/human as a first-class step**.

### Try/catch error branches

Today a failed step either halts the run or (with `continueOnFailure`) lets sibling lanes finish and
settles `Failed`. Neither lets you *handle* the failure. Add an **error edge**: a labelled edge
(`error`) from a step that the router follows when that step fails after its retries are exhausted,
instead of halting.

- **Seam:** `WorkflowEdge` already carries labels for `switch` cases — reserve `error` as an edge
  semantics. `ExecuteStepHandler`'s failure path checks for an outgoing error edge before failing the
  execution; if present, it dispatches that target with the failure exposed as
  `{{steps.<key>.error}}` (message / type / statusCode where available).
- **Builder:** author an "on error →" edge (distinct colour) from any step.
- **Tests-first:** a step that fails routes to its error lane and the run still succeeds; no error
  edge → unchanged (halt / continue-on-failure as today).

### Durable wait / human approval

A `wait` step that **suspends** the execution until resumed — by time (`delaySeconds` / `until`) or by
an external signal (an approval URL or `POST /api/executions/{id}/resume`). This is the feature the
durable engine is uniquely positioned for.

- **Seam:** the durable scheduler already exists (`schedule.workflow`) and executions are durable. Add
  a `Waiting` execution status; the `wait` step persists a resume token and (for timeouts) schedules a
  wake; resume re-enters the engine at the next step with the resume payload as output.
- **Approval UX:** a generated, optionally-signed approval link (reuse `WebhookSecret`); approve/reject
  becomes the step's output for a downstream `gate`/`switch`.
- **Tests-first:** suspend → resume-by-signal continues; suspend → timeout takes the timeout path;
  resume of an already-resumed/again execution is idempotent.

### Retry from a step

Execution detail → "retry from step N": re-dispatch from a chosen step reusing prior step outputs.

- **Seam:** retry already does byte-identical replay on the latest version; this starts mid-graph
  instead of at the root, reusing the completed upstream outputs.

**Risks:** suspend/resume touches execution state machine + redelivery semantics — keep it small and
integration-tested against real Postgres. It's the keystone primitive, so over-test it.

---

## v3.3 — Loops & sub-workflows

Build **sub-workflow call** first; `forEach` is then "map the sub-workflow over an array."

### Sub-workflow as a synchronous step

`workflow.call` runs another workflow, passes inputs, awaits its terminal state, returns its output as
the step output.

- **Seam:** `RunWorkflow` + chaining already start child executions; the new part is the parent
  **awaiting** the child — which reuses v3.2's suspend/resume. Guard recursion with the existing
  `Engine__MaxChainDepth`; never cross workspaces.
- **Tests-first:** parent waits for child success and reads its output; child failure surfaces on the
  parent (routable via the v3.2 error edge); depth cap enforced.

### forEach / map

Run a sub-sequence per array item with a concurrency cap, collecting outputs in order.

- **Model:** dynamic fan-out — one child sub-execution per item (reusing `workflow.call` mechanics), a
  durable counter/join that completes when all items finish, results gathered into an array output.
- **Hard part:** dynamic, durable fan-out/join (the static fan-out/join from branching is the
  template). Cap concurrency; cap item count defensively.
- **Tests-first:** N items → N child runs → ordered results; a failing item respects
  continue-on-failure vs halt; concurrency cap honoured.

**Risks:** durable dynamic fan-out is the hardest engine work in the whole arc. Tests-first, capped,
and consider shipping `workflow.call` alone first if `forEach` needs more bake time.

---

## v3.4 — Connector & trigger breadth

Independent plugins, mostly parallelizable. Each its own commit, tests-first, connection-tester where
a non-intrusive probe exists. Candidates, roughly by value:

- **Database query** — `db.query` (Npgsql is already a dependency; MySQL via its own plugin),
  parameterized queries, a DSN/connection type. High utility for homelab/back-office flows.
- **GitHub** — actions (create issue/comment, repo dispatch) + triggers (issues/PRs via webhook or
  poll) — beyond today's release `feed`. OAuth infra already exists.
- **Google** — Sheets (append/read), Gmail (send), Calendar — over the existing OAuth2 connections.
- **IMAP email-received trigger** — `email.onMessage` listener (MailKit already bundled for SMTP),
  mirroring `matrix.onMessage`.
- **S3 / object storage** — put/get/list against the S3 API.

No engine changes — this is breadth on the existing action/trigger/connection seams.

---

## v3.5 — Ops & hardening

The tier that matters once an instance is exposed or multi-tenant.

- **Out-of-proc plugin sandboxing** — the real isolation boundary. Each plugin loads in its own
  process with its own dependency closure behind a serialized boundary. **This retires the in-proc
  workarounds** (`PluginLoadContext` name rules, `PluginReflection.LoadableTypes` partial-failure
  tolerance) — delete them when it lands rather than carry them. Largest single item; could be its own
  release or slip to v4.
- **Audit log + instance-admin role** — who changed/ran/deleted what; a role above workspace-owner for
  instance operators.
- **Metrics + failure alerting** — finish the OTel/Prometheus export and add a built-in
  "on execution failed → notify" hook (a system-level workflow or engine event).
- **Action idempotency keys** — dedup external side-effects on retry (send/`webhook.send` actions take
  an idempotency key), so a redelivery can't double-send.
- **Per-tenant DEKs + secret rotation** — the `v1:`/`v2:` ciphertext prefix is already in place for
  envelope encryption; finish per-tenant data-encryption keys and a rotation path.

---

## Cross-cutting (pull in opportunistically)

Not a release of their own; fold into whichever release they fit:

- **Per-step test / dry-run** with pinned sample input — biggest builder-iteration win; pairs well with
  v3.2/v3.3 when authoring gets more complex.
- **Workflow variables / environments** — named constants + env-scoped values (dev→prod).
- **Template gallery** — shareable workflow templates beyond the first-party plugin catalog.
- **AutomateX-as-MCP-server** — expose workflows as tools for agents (the inverse of `mcp.call`); the
  AI-native angle, slots wherever the MCP work is warm.

## Discipline (unchanged from v2/v3)

Each milestone is its own commit under a point-release tag; rules/tests first; dogfooded; one design
note per substantial primitive (error edges, durable wait, dynamic fan-out each deserve one before
code). Confirm the first milestone of a release before building it.
