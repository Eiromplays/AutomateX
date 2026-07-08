# Changelog

Notable changes per release, newest first. AutomateX is the v2/v3 rewrite of
[AutomateX-v1](https://github.com/Eiromplays/AutomateX-v1).

## v4.11.0

A focused minor hardening dynamic API keys: each key can now carry a **requests-per-minute cap** and a
**client-IP allowlist**, both enforced at the auth boundary. One migration (two nullable/empty columns);
opt-in, so existing keys are unchanged.

- **Per-key rate limit.** A key can set `rateLimitPerMinute`; requests presenting it are fixed-windowed
  (1 minute) and return **429** over the cap. A global limiter reads the authenticated key from the
  request — no per-endpoint wiring — bucketed by the key's **id** (not its name, which isn't unique), so
  two keys never share an allowance. Any request with no key or no limit passes through untouched, so the
  existing per-IP webhook/auth limiters are unaffected.
- **Per-key IP allowlist.** A key can set `ipAllowlist` — CIDRs and/or bare IPs; a request presenting it
  from any other client IP is refused (**403**) at the gate, covering both `/api` and `/hubs`. The match
  uses the real client IP recovered from the trusted proxy (the v4.6 forwarded-headers pipeline), so it
  can't be spoofed via a raw `X-Forwarded-For`. Malformed entries are rejected when the key is minted
  (400), and parsing fails **closed** — a stored entry that won't parse fences the key rather than
  silently opening it. Behind a proxy, pin `ForwardedHeaders:KnownProxies` to your proxy's IP(s) so the
  client IP can be trusted.
- Both default off (null limit / empty allowlist = today's behaviour). Set them in **Settings → API
  keys** or via `POST /api/keys`. Adds two `ApiKeys` columns; no other schema change. See
  [docs/recipes/api-keys.md](docs/recipes/api-keys.md) and
  [docs/api-key-hardening-design.md](docs/api-key-hardening-design.md).

## v4.10.0

A focused minor closing the long-standing `forEach` follow-up: loop children can now run in bounded
parallel. One migration (a per-item results table).

- **`forEach` runs items in parallel, capped.** A `forEach` step took its array strictly one item at a
  time. A new **`concurrency`** (default `1` = unchanged) runs up to N children at once, clamped to
  `[1, min(itemCount, Engine:ForEachMaxConcurrency)]` (ceiling default `20` so a large array can't fan
  out unbounded). Set it in the builder's forEach editor. Results still come back **in item order** and
  per-item failures still land in their own slot — only the number in flight changes. Under the hood the
  accumulator was rebuilt: instead of one serialized results blob (which re-serialized the whole array on
  every completion — O(N²) — and was a single row every parallel child contended on), each item now has
  its own row in a new `ForEachItemResults` table. A child writes only its own row (contention-free,
  idempotent via the unique `(ExecutionId, StepOrder, ItemIndex)` key); counters advance with atomic
  `UPDATE`s and a single finisher is elected lock-free, so completions never lose a result or double-run.
  Adds the `ForEachItemResults` table and drops the old `ForEachState.ResultsJson`/`NextIndex` columns;
  `concurrency = 1` is byte-for-byte the previous behaviour. See
  [docs/foreach-concurrency-design.md](docs/foreach-concurrency-design.md) and
  [docs/recipes/sub-workflows-and-loops.md](docs/recipes/sub-workflows-and-loops.md).

## v4.9.0

A loose-ends release clearing the two follow-ups that have ridden along since v4.4/v4.5 — a live
plugin-log stream and an app-wide icon pass. Unrelated subsystems, no migration.

- **Plugin logs now tail live over SignalR.** The plugin **Logs** console read its output only through
  the `GET /plugins/{name}/logs?since=` poll, so a burst of output showed up a poll-interval late and a
  live tail flickered. Lines are now pushed to the console as they're appended, over a **dedicated
  `PluginLogsHub`** (`/hubs/plugin-logs`) — separate from the workspace execution hub because the auth
  boundary differs: `JoinPlugin` requires **instance-admin** (mirroring the logs endpoint; logs can carry
  sensitive runtime data), not workspace membership. The engine gains a name-agnostic `IPluginLogSink`
  seam (keyed by plugin dll path, a no-op by default so the engine keeps no SignalR dependency); the Web
  layer's `SignalRPluginLogSink` resolves the dll to the global plugin name and broadcasts to
  `plugin:{name}`. The stream is **additive**: the console snapshots via the existing poll, then tails by
  `Seq`, and the poll stays on as the reconnect backstop — the 500-line ring guarantees a dropped
  best-effort line reappears on the next snapshot, so the socket never has to be reliable. No new config,
  no migration. See [docs/recipes/plugin-operations.md](docs/recipes/plugin-operations.md) and
  [docs/plugin-log-stream-and-icons-design.md](docs/plugin-log-stream-and-icons-design.md).
- **An app-wide icon pass.** The UI leaned on ad-hoc emoji and text glyphs (🔗 ⛓ ✕, status dots, text-only
  nav) that render inconsistently across platforms and don't theme or scale. They're replaced by
  [`lucide-react`](https://lucide.dev) — a tree-shakeable, MIT icon set that pairs with the React +
  Tailwind + Radix stack — applied across the nav, the execution and plugin **status chips**, the
  executions **relationship badges** (chained / retry), and `schema-form`'s inline builder controls
  (reference inserter, add / remove rows, connection/step ref chips). Purely presentational: sizes and
  colours come from the Tailwind classes already in use, no layout change, and icon-only controls keep an
  accessible label. Frontend only — no server change.

## v4.8.0

A loose-ends release closing the two follow-ups from the v4.5.2 / v4.7 arc — an egress allowlist and a
first-class headers editor. Independent subsystems, no migration.

- **The egress guard gains an allowlist.** `Engine:BlockPrivateNetworkRequests` was all-or-nothing: on
  meant *every* private/loopback/link-local target was blocked, so an internet-exposed instance that
  legitimately calls one internal host (a LAN webhook, a self-hosted API) couldn't turn the guard on
  without losing that call. `Engine:EgressAllowlist` is the escape hatch — a list of hostnames and/or
  CIDRs (e.g. `metrics.lan`, `10.8.0.0/24`, `fd00::/8`) that stay reachable while the guard is on. It
  **widens only**: it can permit an otherwise-blocked target but never blocks one the base rule allows,
  so an empty allowlist is exactly the v4.7 behaviour — no regression risk. A hostname is matched against
  the request host (so an internal name works even though it resolves private); a CIDR is matched against
  the IP actually dialed (rebinding-proof, same as the block path). The single `EgressPolicy` in
  `AutomateX.Plugin.Protocol` backs both the in-process engine and the out-of-process plugin hosts, which
  receive it at launch via `AUTOMATEX_EGRESS_ALLOWLIST` alongside the existing block flag. Malformed
  entries are dropped with a logged warning, never fatal. Set it with the comma-separated `EGRESS_ALLOWLIST`
  env (expanded into the array by the app) or an `Engine:EgressAllowlist` array in appsettings. Read at
  child launch, not hot-reloaded — restart to change. No migration.
- **A first-class headers editor in the builder.** `http.poll`, `http.request`, and `webhook.send` all
  carry a `headers` string-map, but the builder only surfaced it through the generic raw-JSON textarea —
  you hand-typed `{"Authorization":"Bearer …"}` and any invalid-JSON keystroke was silently dropped. This
  was the v4.5.2 "trigger editor can't edit `http.poll` headers" follow-up. Headers now render as
  add/remove key/value rows, and the value input keeps the reference inserter so `{{connections.*}}` and
  step references still work in a header value. It's schema-driven, not `http.poll`-specific: any config
  field the exporter emits as a string→string map (`Dictionary<string,string>`) gets the editor, so
  plugins get it for free. Frontend only — no server change.

## v4.7.0

- **The egress guard now covers out-of-process plugin HTTP.** `Engine:BlockPrivateNetworkRequests`
  previously guarded only the engine's own `http.request`/`webhook.send`; a plugin's HTTP — an action,
  a trigger listener, or a connection test — runs in its own process (v4.0) and used an unguarded
  `HttpClient`, so it could still reach loopback / private (RFC1918, ULA) / link-local addresses,
  including the cloud metadata endpoint `169.254.169.254`. The same guard now applies on both sides of
  the plugin boundary: the shared address classifier moves into `AutomateX.Plugin.Protocol` (one
  implementation, no drift between engine and host), and the engine hands each plugin host the policy at
  launch via `AUTOMATEX_EGRESS_BLOCK_PRIVATE`. Same block rule, same Tailscale/CGNAT (`100.64.0.0/10`)
  carve-out, and still **off by default** so LAN/Tailscale targets keep working — turn it on for
  internet-exposed instances. No new config key, no migration. See
  [docs/plugin-egress-guard-design.md](docs/plugin-egress-guard-design.md).

## v4.6.0

- **Dynamic, scoped API keys.** The single shared key is replaced by DB-stored, individually revocable
  keys that each carry a **scope**, a **role**, and an optional **expiry**. Manage them in-app under
  **Settings → API keys** or via `POST/GET/DELETE /api/keys`. A key's token has the form
  `atx_<prefix>_<secret>`; only a SHA-256 hash of the secret is stored, and the full token is shown
  **once** at creation. Presenting a key is unchanged — `X-Api-Key: atx_…` (or the HttpOnly session
  cookie after signing in with it).
- **Three scopes, least privilege.** `Instance` (admin across every workspace), `Workspace` (a role
  within one workspace — an `X-Workspace-Id` naming another is refused), and `Workflow` (a role confined
  to a single workflow: it can't see or touch other workflows, their executions, triggers, or state).
  Instance admins mint any scope; a workspace `Owner` mints keys within their workspace at a role no
  higher than their own.
- **Per-key audit + last-used.** Actions are attributed to `key:<name>` instead of a single `api-key`
  string, and each key records a throttled `LastUsedAt`. Revocation is soft (the row survives for the
  audit trail) and takes effect immediately — the gate re-checks the key every request.
- **MCP gets a real per-workflow credential.** Hand an MCP client a `Workflow`-scoped key and its
  `tools/list`/`tools/call` are limited to exactly that one workflow — the headline use case.
- **⚠ Breaking: `Auth__ApiKey` is removed.** The static shared key no longer exists. OIDC instances are
  unaffected. For a no-IdP setup, set **`Auth__Bootstrap=true`** once: on first run (no OIDC, no keys
  yet) AutomateX mints a single `Instance` admin key and logs it once (`AUTOMATEX BOOTSTRAP KEY: atx_…`).
  Copy it, sign in, mint scoped keys, then revoke the bootstrap key. Scripts keep working — swap the old
  shared value for a newly minted `atx_` key. See [docs/recipes/api-keys.md](docs/recipes/api-keys.md)
  and `RELEASE-v4.6.0.md`. **No data migration** beyond the `ApiKeys` table added in this release.

## v4.5.2

- **Plugin HTTP regains a default User-Agent.** In-process plugins used a shared `HttpClient` that sent
  `User-Agent: AutomateX/2.0`; the v4.0 out-of-process host handed plugins a bare `HttpClient` with no
  UA, which some APIs (notably GitHub) reject with a **403** — silently breaking `http.poll` against
  those hosts (e.g. self-deploy). The plugin host now defaults the same `AutomateX/2.0` UA for trigger,
  action, and connection-test clients; a config-supplied `User-Agent` header still overrides it.
- **`http.poll` logs non-2xx responses.** A poll that gets a 401/403/etc. is still ignored (never
  fires, never moves the baseline) but now logs `http.poll <url> returned <status>` to the plugin's log
  ring — a persistent auth failure is no longer indistinguishable from "nothing changed".
- *Known follow-ups (now closed):* the trigger editor couldn't edit `http.poll` headers from the UI — a
  first-class key/value headers editor lands in v4.8.0. (The out-of-process plugin HTTP SSRF/egress
  bypass noted here was closed in v4.7.0.)

## v4.5.1

- **Out-of-process trigger listeners now self-heal.** A plugin trigger whose `RunAsync` threw (e.g. a
  transient `Resource temporarily unavailable` resolving an upstream host) was killed and never
  restarted — in-process listeners retry, out-of-process ones didn't. A single network blip could
  silently stop a poll trigger (e.g. self-deploy) until the app restarted. The plugin host now retries
  a failed listener with backoff, mirroring the in-process loop.
- **Listener failures are visible.** Out-of-process `trigger.error` now stamps the trigger's
  `LastError`/`LastErrorAt` (cleared on the next successful fire), not just the plugin log ring.
- **Connection edits recycle dependent listeners.** Trigger `{{connections.*}}` are resolved once at
  listener start; a per-workspace connection version is now folded into the listener fingerprint, so
  editing a connection (e.g. rotating an expired token) re-resolves running listeners instead of
  requiring a restart. No migration.

## v4.5.0

- **AutomateX as an MCP server.** Expose opted-in workflows as MCP tools so Claude and other MCP
  clients can call them. Flag a workflow **"Expose as MCP tool"** (with a name + description) and it
  shows up in `tools/list`; a `tools/call` runs it with the given `payload` as the trigger payload,
  waits (bounded by `Engine__McpCallTimeout`, ~60s) for it to settle, and returns the final step's
  output — or the execution id if it's still running. JSON-RPC over **`POST /api/mcp`**, off unless
  `Engine__AllowMcpServer=true` **and** an API key is configured (it refuses in open mode — exposing
  workflows to a model must be deliberate); workspace-scoped via `X-Workspace-Id`; runs are tagged
  `mcp:<tool>` for audit. Adds `McpExposed`/`McpToolName`/`McpDescription` columns to the workflow. See
  [docs/recipes/mcp-server.md](docs/recipes/mcp-server.md) and
  [docs/mcp-server-design.md](docs/mcp-server-design.md).

## v4.4.0

- **Plugin operations.** The out-of-process plugin boundary is now an operations surface, on the
  Plugins page.
  - *Observability:* each plugin shows a live status (running / crashed / idle / disabled, uptime,
    restart count) and memory, plus an expandable **console** — a bounded per-plugin log ring buffer,
    polled every few seconds (logs are instance-admin only, as they can carry sensitive data).
  - *Controls (instance-admin):* **restart** a plugin's host process, and **disable/enable** it — a
    disabled plugin's actions, triggers, and connection types disappear until re-enabled (a `.disabled`
    marker file; no DB).
  - *Resource limits (opt-in, default off):* `Engine__PluginLimits__CallTimeout` (cancel a long call),
    `__MaxMemoryBytes` (a watchdog kills + restarts an over-budget host), and `__MaxConcurrentCalls`.
  - New endpoints under `/plugins/{name}`: `status`, `logs`, `restart`, `enabled`. No migration. See
    [docs/recipes/plugin-operations.md](docs/recipes/plugin-operations.md) and
    [docs/plugin-operations-design.md](docs/plugin-operations-design.md).

## v4.3.0

- **Template gallery.** Start a workflow from a ready-made one, from three sources on a single page:
  **built-in** starters (shipped, offline), **your templates** (save any workflow as a reusable,
  workspace-shared template — stored as its portable export doc, secrets excluded), and a **community**
  catalog (a release-published `templates-catalog.json` of contributed workflow docs, fetched on
  demand). Every "Use" flows through the existing import-review screen; the gallery has search + a
  category filter across all sources; community templates can be saved to your own. Adds the
  `WorkflowTemplates` table and `Engine__TemplateCatalogUrl`. See
  [docs/recipes/templates.md](docs/recipes/templates.md) and
  [docs/template-gallery-design.md](docs/template-gallery-design.md).

## v4.2.0

- **Workflow variables & environments.** Reusable values referenced as `{{vars.<name>}}`, defined per
  **workspace** (shared by every workflow) or **per workflow** (an override that shadows a workspace
  variable of the same name). Each variable holds a value per **environment** (e.g. `default` /
  `staging` / `prod`); a run resolves against the workspace's active environment, with a per-run
  override and a stamp of the chosen environment on the execution. **Secret** variables are encrypted
  at rest (workspace DEK) and masked everywhere, like connections; plain values are shown in clear.
  Manage them on the new **Variables** page; `{{vars.x}}` joins the builder autocomplete; preview shows
  vars (secrets masked) and a real single-step run uses live values. Variables ride exported workflows
  as name references (the importing instance needs same-named variables/environments), the same as
  connections. Adds the `WorkspaceEnvironments`, `Variables`, `VariableValues` tables, a workspace
  active-environment column, and an execution environment stamp. See
  [docs/recipes/variables.md](docs/recipes/variables.md) and
  [docs/workflow-variables-design.md](docs/workflow-variables-design.md).

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
