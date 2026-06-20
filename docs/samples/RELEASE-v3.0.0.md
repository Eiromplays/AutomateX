# AutomateX v3.0.0

The release-candidate line that turned the v2 engine into a product you run 24/7 and extend from the
browser — durable by default, self-hostable, and hardened.

## Highlights

- **Homelab deployment.** First-class self-hosting: GHCR images (`automatex-api`/`automatex-web`)
  published from `release.yml` on `v*` tags, `docker-compose.prod.yml` + `.env`, a Proxmox + Docker +
  Tailscale Serve (automatic HTTPS) walkthrough, OIDC behind a reverse proxy, named-volume plugins
  with a chown sidecar, and a `GET /api/version` badge. See
  [docs/deploy-homelab.md](docs/deploy-homelab.md).
- **Trigger → lane routing.** A trigger can start a run at a specific step (`EntryStepOrder`), so one
  workflow hosts several entry points; the builder/graph author and draw the trigger→step edge.
- **Branching & parallel, finished.** Continue-on-failure, builder fan-out authoring, and join
  semantics on top of the edge-DAG router.
- **Feed triggers.** `AutomateX.Plugins.Feed` adds `rss` and `http.poll` (configurable headers, fires
  only on 2xx, dedups on content hash, exposes the parsed JSON body as `{{trigger.payload.json…}}`).
  Powers the pull-model [self-deploy](docs/recipes/self-deploy.md).
- **KV actions.** Built-in `kv.get` / `kv.set` / `kv.setIfAbsent` / `kv.delete` over the durable
  per-workflow store — `setIfAbsent` + `gate` is the run-once dedup primitive. See
  [docs/recipes/dedup-and-state.md](docs/recipes/dedup-and-state.md).
- **Builder UX.** Shared connection form across the page and the in-builder modal; searchable
  connection picker and Connections-list search; multiline config fields (`[Multiline]`) as
  auto-growing textareas; connection-reference validation (green resolves / amber unknown);
  required-field hints; and inline `{{connections.…}}` autocomplete in text and multiline fields.
- **Workflow lifecycle.** Enable/disable (a true pause — disabled workflows are dropped at the
  engine, so no trigger fires them); clone; cancel/back on the edit page; delete unreferenced past
  versions (the latest and any execution-referenced version are protected); loading skeletons.
- **Dashboard depth.** Execution metrics, per-workflow health, recent-failures panel, and executions
  pagination.

## Security hardening

- **Webhook auth is now HMAC.** Verify with `X-Webhook-Signature: sha256=<hex>` over the raw body
  (preferred) or the `X-Webhook-Secret` header. The `?secret=` query parameter has been **removed**.
- **SSRF guard** (opt-in via `Engine__BlockPrivateNetworkRequests`) blocks loopback / RFC1918 / ULA /
  link-local / `0.0.0.0/8` on both action and trigger HTTP clients, with a DNS-rebinding-safe connect
  callback. CGNAT `100.64/10` stays allowed for Tailscale.
- **Rate limiting** per resolved client IP, with forwarded headers trusted from known proxies in all
  auth modes.
- **CI hardening:** Biome lint for the web app, Dependabot, and CodeQL.

## Upgrade notes

- **Breaking — webhooks:** switch any `?secret=` callers to the HMAC `X-Webhook-Signature` header (or
  the plaintext `X-Webhook-Secret` header). See [SECURITY.md](SECURITY.md).
- **Migrations** apply on startup by default (`Database__MigrateOnStartup`). Back up Postgres first.
- **Plugin upload** stays gated behind `Engine__AllowPluginUpload` (uploaded plugins run in-process
  with full host trust).
- `Encryption__Key` is never stored — keep it stable across deploys or encrypted connections won't
  decrypt.

Full history: [CHANGELOG.md](CHANGELOG.md). v1 is archived at
[AutomateX-v1](https://github.com/Eiromplays/AutomateX-v1).
