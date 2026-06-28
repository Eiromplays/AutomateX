# Changelog

Notable changes per release, newest first. AutomateX is the v2/v3 rewrite of
[AutomateX-v1](https://github.com/Eiromplays/AutomateX-v1).

## v4.1.0

- **Per-step preview / test.** Tighten the authoring loop on a single step without running the whole
  workflow. **Preview** (default, zero side effects) resolves the step's current — even unsaved —
  config against an optional sample context, reports *every* unresolved reference at once, and masks
  connection values while listing which fields the step reads. **Run for real** (opt-in, confirm-gated,
  editor-only) executes that one leaf action once and shows its output or error — no execution rows,
  chaining, retries, or idempotency; control-flow nodes (`switch`/`forEach`/`wait`/`workflow.call`)
  are refused, and a real run is audited (`step.test`). Both live behind a **Preview / test** panel on
  each step in the builder (form + canvas). New endpoints `POST /api/workflows/{id}/preview-step` and
  `/test-step`; no schema change. See [docs/recipes/per-step-preview.md](docs/recipes/per-step-preview.md)
  and [docs/per-step-dry-run-design.md](docs/per-step-dry-run-design.md).

## v4.0.0

- **Out-of-process plugin sandboxing (breaking).** Plugins no longer load into the engine — each runs
  in its own `AutomateX.PluginHost` child process with its own `AssemblyLoadContext` and dependency
  closure, addressed over a stdio JSON protocol. A plugin can no longer read engine memory, crash the
  host, or collide on dependency versions. The engine discovers actions/triggers/connection types by
  *describing* each host and marshals execution, OAuth-config, credential tests, and trigger listening
  across the boundary. Out-of-proc is now the only mode (`Engine__OutOfProcPlugins` defaults on); the
  in-proc loader (`PluginLoadContext`, shadow-copy) is removed.
- **Bundled host.** The API image ships `AutomateX.PluginHost` under `pluginhost/` — no operator
  action needed. `Engine__PluginHostPath` overrides the location.
- **Hardened reload.** Installing/updating a plugin recycles its host process, so new code always wins.
- **Breaking for plugin authors:**
  - Plugin **event listeners** (`IEngineEventListener` inside a plugin) are no longer supported — the
    protocol has no listener channel. Actions, triggers, and connection types are unchanged.
  - Plugin types are constructed via their longest constructor with **optional parameters defaulted**;
    a *required* constructor dependency is rejected. Take services from `ActionContext`
    (`Logger`/`Http`), not constructor injection.
  - Deploy unchanged in shape: `plugins/<Name>/<Name>.dll` **plus its dependency dlls in the same
    folder** (a normal `dotnet publish`/`build`, or the catalog zip — both already include them).
- Plugin management (list/install/upload) is path-based now; the reload fingerprint is a dll
  content-hash. See [docs/plugin-sandboxing-design.md](docs/plugin-sandboxing-design.md) and
  [RELEASE-v4.0.0](docs/samples/RELEASE-v4.0.0.md).

## v3.8.0

- **Retention pruning.** Optional windows bound the growth of the append-only/cache tables: a
  background sweeper deletes audit entries past `Engine__AuditRetention` and idempotency records past
  `Engine__IdempotencyRetention` (both opt-in — unset keeps them forever, joining the existing
  `Engine__ExecutionRetention`). No schema change. See
  [docs/recipes/audit-log.md](docs/recipes/audit-log.md) and
  [docs/recipes/idempotency.md](docs/recipes/idempotency.md).

## v3.7.0

- **Per-tenant encryption keys.** Connection secrets are now encrypted with a per-workspace
  data-encryption key (DEK) wrapped by the instance key (`Encryption__Key`, the KEK) — so a single
  compromised key exposes one workspace, not the whole instance. Transparent and backward-compatible:
  existing `v1:` ciphertext keeps decrypting, new writes use `v2:` per-tenant.
- **Key rotation.** Rotate a workspace's DEK (re-encrypts its connections) or re-wrap every DEK under
  a new instance key — from the workspace settings page (instance-admin only) or
  `POST /api/workspaces/{id}/rotate-key` / `POST /api/keys/rewrap`. A KEK change is bridged by
  `Encryption__PreviousKey` so old-wrapped data still reads during the swap. Rotations are audited.
  Adds the `WorkspaceKeys` table. See [docs/recipes/key-rotation.md](docs/recipes/key-rotation.md) and
  [docs/per-tenant-deks-design.md](docs/per-tenant-deks-design.md).

## v3.6.0

- **Audit log.** An append-only trail of who did what: every config mutation (workflow, connection,
  trigger, workspace, member, plugin create/update/delete) is recorded with actor, action, target and
  a short summary, and every execution settle logs `execution.succeeded`/`execution.failed`. Read it
  at `GET /api/audit` or the new **Audit** page — members see their workspace, instance-admins see all
  (filter by actor/action). Adds the `AuditEntries` table. See
  [docs/recipes/audit-log.md](docs/recipes/audit-log.md) and
  [docs/audit-and-admin-design.md](docs/audit-and-admin-design.md).
- **Instance-admin role.** A role above workspace-owner for operators, granted via config
  (`Auth__InstanceAdmins` = OIDC subjects/emails; open and api-key callers are operators by default).
  Instance-admins read the audit log across all workspaces.

## v3.5.1

- **Action idempotency keys.** A step can carry a templated **idempotency key** (e.g.
  `{{trigger.payload.orderId}}`); the engine caches the first successful result keyed per workflow and
  returns it on any later run with the same key — without re-invoking the action. Dedups re-fires of
  the same logical event and post-commit redeliveries; failures aren't cached. `webhook.send` also
  forwards the key as an `Idempotency-Key` header so a compliant receiver dedups retries too. Authored
  per-step in the builder; travels with export/import. Migration `AddIdempotency`. See
  [docs/recipes/idempotency.md](docs/recipes/idempotency.md) and
  [docs/idempotency-design.md](docs/idempotency-design.md).

## v3.5.0

- **Failure alerting (`execution.onFailure` trigger).** A workspace-wide subscriber: when any
  execution settles `Failed`, every enabled `execution.onFailure` trigger's workflow runs with a
  failure summary as `{{trigger.payload}}` (`workflowName`, `failedStep.{key,actionType,error}`, and a
  `url` when `Engine__PublicBaseUrl` is set) — so the alert workflow notifies however it likes
  (`matrix.send`, `slack.send`, `webhook.send`, open a ticket). Collected on the durable terminal path
  so alerts ride the outbox (crash-safe). Loop-guarded: an alert run never re-alerts (self-exclusion),
  sub-workflow/`forEach` children are suppressed unless `includeSubWorkflows`, and an optional
  `watchWorkflowId` scopes the subscription to one source. Authored in the builder; exports portably.
  See [docs/recipes/failure-alerting.md](docs/recipes/failure-alerting.md).
- **Metrics (OpenTelemetry + Prometheus).** Domain instruments — `automatex.executions.started`,
  `automatex.executions.settled`, `automatex.execution.duration`, `automatex.steps.settled` (bounded
  `status`/`action`/`trigger` tags) — exported via OTLP (push, on `OTEL_EXPORTER_OTLP_ENDPOINT`) and a
  Prometheus scrape at `/metrics` (pull, `Metrics__EnablePrometheus`, default on, outside the API-key
  gate). See [docs/metrics-and-alerting-design.md](docs/metrics-and-alerting-design.md).

## v3.4.0

- **Sub-workflows (`workflow.call`).** Run another workflow as a step and wait for it: the parent
  suspends, the child runs, and the child's result `{status, executionId, output}` becomes the call
  step's output — branch on `{{steps.<key>.output.status}}`. Reuses the durable suspend/resume, so a
  paused parent survives restarts. Workspace-isolated and depth-guarded (`MaxChainDepth`); the
  execution page links parent↔child.
- **Loops (`forEach`).** Map a workflow over an array — each item becomes the child's
  `{{trigger.payload}}` — collecting the results in order as the step output. Runs sequentially in
  this release (durable per-item accumulator); a concurrency cap is planned. See
  [docs/recipes/sub-workflows-and-loops.md](docs/recipes/sub-workflows-and-loops.md),
  [docs/sub-workflows-design.md](docs/sub-workflows-design.md), and
  [docs/foreach-design.md](docs/foreach-design.md).

## v3.3.0

- **Durable wait / human approval.** A new `wait` step suspends the run into a `Waiting` status —
  resumed by a timer (`delaySeconds`/`until`), or by an external signal (an approval) with an optional
  `timeoutSeconds`. The run survives restarts (it rides the durable scheduler) and the resume payload
  becomes the wait step's output, so a downstream `gate`/`switch` can branch on the decision. Resume
  from the UI (a Resume button on the execution page) or `POST /api/executions/{id}/resume`. See
  [docs/recipes/approvals-and-waits.md](docs/recipes/approvals-and-waits.md) and
  [docs/durable-wait-design.md](docs/durable-wait-design.md).
- **Retry from a step.** Re-run a finished execution starting at a chosen step, reusing the earlier
  steps' outputs (pinned to that run's version) — a "↻ Retry from here" action in the step inspector,
  or `POST /api/executions/{id}/retry-from/{order}`.

## v3.2.0

- **Try/catch error branches.** A step can have an **error edge** — give it an "on error → step"
  target in the builder and a failure (after its retries) routes down that lane instead of failing
  the run. The caught step is recorded `Caught` (not `Failed`, shown orange in the timeline), and the
  failure is addressable on the error lane as `{{steps.<key>.error.message}}`. Error handling wins
  over both halt and continue-on-failure; the execution settles on the error lane's outcome. No
  schema change — `"error"` is a reserved edge label (like switch's `"default"`), one per step. See
  [docs/recipes/error-handling.md](docs/recipes/error-handling.md) and
  [docs/error-branches-design.md](docs/error-branches-design.md).

## v3.1.0

- **Named step references.** Steps get a stable `key` (slugged from the name, unique per version)
  as the reference identity, so `{{steps.<key>.output.<field>}}` survives both rename and reorder —
  the positional `{{steps.<order>…}}` form still works. The builder validates refs inline (resolves
  / fragile-index / unknown), offers a one-click "convert index refs → names", and autocompletes
  `{{steps.<key>.output.<field>}}` from each upstream action's result schema. The save path rejects
  references that can never resolve. See [docs/steps-references-design.md](docs/steps-references-design.md).
- **transform action.** Built-in `transform` reshapes/extracts JSON between steps with a JMESPath
  query (filters, multiselect hashes, functions) — the result becomes the step output directly.
- **webhook.send action.** Built-in outbound webhook: POST a payload with optional HMAC-SHA256
  signing that matches AutomateX's own inbound verification (`X-Webhook-Signature: sha256=<hex>`),
  SSRF-guarded, fails on non-2xx. See [docs/recipes/transform-and-webhooks.md](docs/recipes/transform-and-webhooks.md).
- **Slack & Telegram.** `slack.send` (incoming webhook) and `telegram.send` (Bot API, token verified
  via `getMe`) join the notification plugins.
- **React Router v8.** Web app upgraded to React Router 8 (Vite 8, TypeScript 6, Node baseline 22.22);
  unified the builder's `{{…}}` reference inserter (step outputs + connections in one tabbed panel).

## v3.0.0

The release-candidate line that turned the v2 engine into a product you run 24/7 and extend from
the browser.

- **Homelab deployment.** First-class self-hosting: GHCR images (`automatex-api`/`automatex-web`)
  published from `release.yml` on `v*` tags, `docker-compose.prod.yml` + `.env`, a Proxmox + Docker
  + Tailscale Serve (automatic HTTPS) walkthrough, OIDC behind a reverse proxy, named-volume plugins
  with a chown sidecar, and a `GET /api/version` badge. See
  [docs/deploy-homelab.md](docs/deploy-homelab.md).
- **Trigger → lane routing.** A trigger can start a run at a specific step (`EntryStepOrder`), so one
  workflow can host several entry points; the builder/graph author and draw the trigger→step edge.
- **Branching & parallel, finished.** Continue-on-failure, builder fan-out authoring, and join
  semantics on top of the edge-DAG router.
- **Feed triggers.** `AutomateX.Plugins.Feed` adds `rss` and `http.poll` (configurable headers,
  fires only on 2xx, dedups on content hash, exposes the parsed JSON body as
  `{{trigger.payload.json…}}`). Powers the pull-model [self-deploy](docs/recipes/self-deploy.md).
- **KV actions.** Built-in `kv.get` / `kv.set` / `kv.setIfAbsent` / `kv.delete` over the durable
  per-workflow store — `setIfAbsent` + `gate` is the dedup primitive (run once per key/tag/day).
  See [docs/recipes/dedup-and-state.md](docs/recipes/dedup-and-state.md).
- **Builder UX.** Shared connection form across the page and the in-builder modal; searchable
  connection picker and Connections-list search; multiline config fields (`[Multiline]`) as
  auto-growing textareas; connection-reference validation (green resolves / amber unknown);
  required-field hints; and inline `{{connections.…}}` autocomplete in text and multiline fields.
- **Workflow lifecycle.** Enable/disable as a true pause (disabled workflows are dropped at the
  engine, so no trigger fires them); clone; cancel/back on the edit page; delete unreferenced past
  versions (the latest and any execution-referenced version are protected); loading skeletons.
- **Security hardening.** Webhook auth moved to HMAC (`X-Webhook-Signature: sha256=<hex>` over the
  raw body, or the `X-Webhook-Secret` header; the `?secret=` query param was removed); opt-in SSRF
  guard (`Engine__BlockPrivateNetworkRequests`) over loopback/RFC1918/ULA/link-local/`0.0.0.0/8`
  with a DNS-rebinding-safe connect callback (CGNAT `100.64/10` stays allowed for Tailscale);
  per-client-IP rate limiting with forwarded headers trusted from known proxies in all auth modes;
  zip-slip guard on plugin extraction; and CI hardening (Biome lint, Dependabot, CodeQL).

## v2.9 — convergence

The big pre-v3 release. `matrix.onMessage` (talk to your homelab) and **scheduled workflows** — a
`schedule.workflow` action exposing the engine's durable scheduler so any workflow can queue a
future run of another (`delaySeconds`/`runAt` + payload, one-shot, workspace-bound). Composed, they
make [natural-language reminders](docs/recipes/reminders.md) real. Connection types (guided
third-party setup) and SignalR workspace groups (events route per-workspace, patched in place). Plus
plugins-page tabs, editable triggers, **conditionals** (the `gate` built-in —
[recipe](docs/recipes/conditional-gate.md)), and **refresh-token sessions** (OIDC `offline_access`;
the session's liveness tracks the IdP instead of sliding blindly).

Earlier in v2.9: **the ears** — `matrix.onMessage`, a sync long-polling trigger (own messages never
trigger, pre-start history skipped, optional room filter), enabled by trigger configs resolving
`{{connections.<name>.<field>}}` at listener start. Payoff: [jarvis-lite](docs/recipes/jarvis-lite.md).

## v2.8 — the plugin platform, finished

Plugins contribute **trigger types**: implement `ITriggerListener<TConfig>` + `[Trigger]`, call
`context.FireAsync(payload)` from the listener (return = poll cycle, throw = restart with backoff);
the engine supervises one listener per enabled trigger row. Trigger config schemas render as real
forms. The **plugin catalog**: releases publish each first-party plugin as a zip + `catalog.json`;
the Actions page lists them with one-click **Install** — downloaded, sha256-verified before touching
disk, extracted and hot-reloaded, behind `Engine__AllowPluginUpload`.

## v2.7 — llm.prompt, export/import, retry

OpenAI-compatible LLM action (optional apiKey for local endpoints). Portable workflow documents —
secrets excluded by construction, review-before-create import. Execution retry: byte-identical
payload replay on the latest version, lineage everywhere.

## v2.6 — workflow chaining

A `workflow` trigger ("when workflow X succeeds / fails / finishes") fires with the source execution
as payload. Chained dispatches ride the durable outbox (crash-safe), never cross workspaces, and are
capped by `Engine__MaxChainDepth` (default 5).

## v2.5 — guardrails & receipts

In-use delete guards for plugins and connections (named blockers, `force=true` override),
schema-drift warnings, app-wide toasts with clean error envelopes, plugin build fingerprints, and a
self-deploy trigger step in the release workflow.

## v2.4 — live plugin platform

Plugins **hot-reload**: `POST /api/actions/reload` re-scans and atomically swaps assemblies, action
registry and event subscriptions; in-flight executions finish on the code they started with. Loads
are shadow-copied; the manager shows each plugin's MVID. Plugins can be **workspace-scoped**. Upload
(zip of the publish output) is gated behind `Engine__AllowPluginUpload`. Also: `http.request` v2
(templated headers, `contentType`, `timeoutSeconds`, opt-in `failOnErrorStatus`, response headers in
the result) and `matrix.send` `msgType`.

## v2.3 — workflow lifecycle

Workflows editable/deletable from the UI: editing **appends an immutable version** so past
executions keep what they ran; deleting removes versions/steps/triggers/history atomically. Past
versions are listed and can be **restored** (git-revert style).

## v2.2 — the platform deploys itself

First-party plugins under `src/Plugins`: **`ssh.command`** (SSH.NET; key/password auth, optional
host-key pinning, captures exit/stdout/stderr) and **`matrix.send`** (deterministic transaction ids,
so retries can't double-send). Together: GitHub release → webhook → detached
`docker compose pull && up -d` over a forced-command key → Matrix announcement
([self-deploy](docs/recipes/self-deploy.md)). SSH is integration-tested against `testcontainers/sshd`.

## v2.1 — workspaces

Workflows, connections and executions live in **workspaces** with invitable members and
viewer/editor/owner roles. Requests scope via `X-Workspace-Id` (absent = Default); connection
resolution is workspace-isolated in the engine. The `Default` workspace adopts pre-workspace data and
can't be deleted.

## v2.0 — auth

Tri-state auth: **open** → **API key** (`X-Api-Key`; cookie exchange in the UI) → **OIDC**
(`Auth__Authority`/`ClientId`/`ClientSecret`; the API owns the code flow, the browser holds only an
HttpOnly cookie).

## v1.3 — connection edits & webhook secrets

`PUT /api/connections/{id}` with merge semantics; per-trigger webhook secrets generated server-side
and shown once; `/api/webhooks` moved outside the global API-key gate.

## v1.2 — connections & secret masking

Secrets move into named **connections**: AES-256-GCM bundles (master key from `Encryption__Key`,
never stored) referenced via `{{connections.<name>.<field>}}`, decrypted only at execution time, and
**masked** (`***`) everywhere persisted or published. Opt-in execution retention.

## v1.1 — templating

Step configs are templates: `{{trigger.payload.x}}`, `{{steps.0.output.y}}` with JSON types
preserved; template errors fail the step instantly (no retries).

## M4 — ship

Self-hostable with `docker compose up`: API as an SDK-built container, SPA as a Caddy image, Postgres
alongside. Plugins from a volume-mounted folder; migrations on startup; optional API-key gate.

## M3 — UI

React Router v7 SPA (React 19, TanStack Query, Tailwind 4): workflow list + builder with config forms
generated from action JSON schemas, trigger management, live execution view over SignalR.

## M2.5 — platform polish

Plugins can **listen** to engine lifecycle events (`IListenFor<TEvent>`), best-effort and in-process
with per-listener fault isolation. One shared `AddAutomateXEngine(...)` composition for app and tests.

## M2 — plugin SDK

Actions are plugin-contributed: `IAction<TConfig, TResult>` + `[Action]`, loaded from `plugins/` in a
collectible `AssemblyLoadContext`. Config/result types exported as JSON Schema via `GET /api/actions`.

## M1.5 — durable engine

Workflows in Postgres as immutable versions; cron/webhook/manual triggers; each step a durable
Wolverine message with per-step retries + backoff. Crash-resume via the durable inbox, atomic cron
lease, EF Core outbox, stuck-execution sweeper. Covered by integration tests against real Postgres.
