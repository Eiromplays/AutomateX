# Product

<!-- impeccable:product-schema 1 -->

## Platform

web

## Users

The primary user is a technically fluent self-hoster running AutomateX on their own
hardware. The product supports teams — workspaces, viewer/editor/owner roles, an
instance-operator role above workspace-owner, and an append-only audit log — and those
surfaces are genuinely exercised on shared instances, but **the typical install is one
person**. Team capability must not drive the default experience.

High technical fluency can be assumed: users are comfortable with cron expressions,
JSON Schema forms, JMESPath transforms, webhooks, HMAC signing, and `docker compose`.

## Product Purpose

A self-hostable, .NET-native automation engine. Users build **workflows** — sequences and
branches of **steps** fired by **triggers** — in a visual builder, run them on a durable
engine that survives restarts, and extend the system with plugins.

Success is workflows running correctly unattended over long periods, and failures being
noticed and diagnosable when they happen.

## Positioning

"Think Zapier/n8n, but yours: your hardware, your data, your code."

The mechanisms a neighboring product could not truthfully copy:

- Every step is a Postgres-backed Wolverine message with per-step retries and backoff;
  crashes resume from the durable inbox rather than restarting the run.
- Cron fires via an atomic lease, so a multi-node deployment cannot double-fire.
- Plugins run **out-of-process** in their own sandboxed host with their own dependency
  closure — a plugin can neither crash nor read the engine.
- Per-workspace data-encryption keys wrapped by an instance key, with one-click rotation
  and instance-key re-wrap.

## Operating Context

Self-hosted on homelab hardware via `docker compose up`, with GHCR images published on
`v*` tags. Workflows run unattended around the clock; the operator returns periodically to
check run health, investigate a failure, or author a new workflow.

Authentication is a ladder: open → API key → OIDC with refresh-token sessions. Metrics
export over OTLP with a `/metrics` scrape for Prometheus/OpenTelemetry.

Two jobs carry **equal weight** and neither may be treated as secondary:

- **Authoring** — canvas, step forms, connection references, templates.
- **Operating** — dashboard, executions, run graphs, failure alerting, audit.

Typical usage is authoring bursts followed by long operating stretches.

## Capabilities and Constraints

**Workflow model** — steps and triggers over an edge-DAG; `switch`/`gate` routing;
parallel fan-out lanes that rejoin; try/catch error branches (an "on error →" edge catches
a step failure into a handler lane, exposing `{{steps.<key>.error}}`); continue-on-failure.

**Triggers** — cron, webhook (per-trigger capability secrets), manual, workflow-chaining,
`execution.onFailure`, and plugin triggers (`rss`, `http.poll`, `matrix.onMessage`).

**Actions** — built-in `http.request`, `webhook.send` (HMAC-signed), `gate`, `switch`,
`transform` (JMESPath), `wait`, `workflow.call`, `forEach`, `kv.*`, `schedule.workflow`,
`llm.prompt`, `llm.agent`, `mcp.call`; first-party plugins `ssh.command`, `matrix.send`,
`discord.send`, `slack.send`, `telegram.send`, `pushover.send`, `email.send`.

**Run states** — durable `wait` steps suspend a run (timer or human approval) into a
`Waiting` status that survives restarts, resumable from UI or API; "retry from a step"
reuses earlier outputs; idempotency keys cache a step's first success.

**Other capabilities** — durable per-workflow KV store; encrypted connections
(AES-256-GCM secret bundles plus OAuth2) referenced as `{{connections.<name>.<field>}}`
and masked everywhere; workspace-scoped plugins with hot-reload and an in-app catalog
using sha256-verified installs and signature-derived first-party trust.

**Design constraints:**

- Action configuration forms are **generated from each action's JSON Schema**. Field
  rendering is generic and shared across every action, so per-action bespoke form layout
  is not available — improvements must land in the schema-driven renderer.
- The run graph and workflow canvas are rendered by `@xyflow/react`, which owns its own
  DOM and styling surface.
- Plugin-contributed actions, triggers, and connection types are open-ended: the UI cannot
  assume a fixed catalog of step types, icons, or category labels.

**Terminology** — workflow, step, trigger, action, connection, variable, execution (run),
workspace, plugin, lane, edge.

## Brand Commitments

The name **AutomateX**, set as a wordmark with the trailing `X` accented.

The voice is ownership-first and plain-spoken: it addresses a competent operator directly
and emphasizes that the instance, the data, and the code belong to the user. It does not
hedge or over-explain.

## Evidence on Hand

Real assets in the repository:

- Screenshots: `docs/img/builder-branching.png`, `docs/img/dashboard.png`,
  `docs/img/execution-graph.png`, `docs/img/form-editor.png`
- 15 task recipes under `docs/recipes/`
- Design notes for most subsystems under `docs/`
- `CHANGELOG.md`; v1 archived at the `AutomateX-v1` repository

**Absences that future work must not fabricate:** there are no testimonials, named
customers, usage statistics, benchmarks, pricing, or case studies. None may be invented.

## Product Principles

1. **Ownership over convenience.** Self-hosted by definition. Nothing may require a hosted
   service or phone home.
2. **Durability is the promise.** The engine survives restarts; the interface's job is to
   make run state, failure, and recovery legible.
3. **Solo-first, team-capable.** The single-operator path is the default. Shared surfaces
   must be available without taxing the common case.
4. **Extension without risk.** Plugins are sandboxed and their provenance is verified; the
   interface must convey trust provenance rather than hiding it.
5. **Authoring and operating are equal.** Neither the builder nor the run views may be
   treated as the secondary surface.

## Accessibility & Inclusion

**WCAG AA is the bar** — a durable constraint, not an aspiration. Body text must hold
4.5:1 contrast; large text and UI components 3:1. Design decisions that cannot meet it must
be changed rather than accepted.

Known outstanding gaps as of this record: disabled buttons dim label and background
together via `disabled:opacity-50` (measured 1.1–1.2:1), and form placeholders sit at
4.1:1. Both are known failures awaiting fixes, not accepted exceptions.
