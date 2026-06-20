# Changelog

Notable changes per release, newest first. AutomateX is the v2/v3 rewrite of
[AutomateX-v1](https://github.com/Eiromplays/AutomateX-v1).

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
