# AutomateX v3 — The Public Release

**Status:** planning draft v0.1 · **Target:** .NET 10 (LTS) / Aspire 13 · **Date:** June 2026 · **Predecessor:** [v2-plan.md](v2-plan.md) (v2.0 → v2.9.2 shipped)

---

## TL;DR

v3 is the release where **the repo goes public**. The bar is no longer "the smallest thing I'd genuinely run" (v2 cleared that) — it's **"the thing I'd put my name on in public and a stranger could set up in ten minutes and immediately get value from."** That means three things land together: a **genuinely good UI** (dashboards, stats, a visual builder, an execution inspector worth opening), **durable feed triggers + state** (the missing primitive that turns AutomateX from request/response into a thing that watches the world), and **enough integration breadth** (Discord + email beside Matrix) that the first workflow a newcomer builds actually reaches them.

Scope is deliberately a **focused public MVP**, not a kitchen sink. The big recorded arcs — OAuth connection types, MCP both directions, a bounded `llm.agent`, and the security-hardening set — are real and wanted, but they are **explicitly deferred to v3.1/v3.2** so v3 can ship. They are not cut; they are sequenced (§7).

The cadence is unchanged from v2: **tests and rules first**, each feature its own commit under a single umbrella tag, dogfooded before it counts as done.

The personal success criterion — the v3 equivalent of v2's "smallest thing I'd run" — is **one workflow that pings me the moment the Edmonton Oilers or Storhamar move** (§5). If that runs reliably for a month, the trigger/state foundation is proven.

---

## 1. Scope decision — focused public MVP

Decided (June 2026): v3 ships **four thrusts** and parks the rest.

In:
1. **UI 2.0** — the headline; the most visible change and the thing a public visitor judges first (§2).
2. **Triggers & durable state** — the new core primitive; powers the hockey use case and far more (§3).
3. **Notification breadth** — Discord + email/SMTP beside Matrix, so the first workflow reaches a newcomer (§4).
4. **Release engineering & docs** — because public ≠ "flip the visibility toggle" (§6).

Deferred to v3.x (sequenced, not cut): OAuth connections + field provenance, MCP server + `mcp.call`, `llm.agent`, branching beyond `gate`, and the full hardening set (out-of-proc sandboxing, audit log, rate limiting, instance-admin role, envelope encryption, idempotency keys). See §7.

Rationale: a public release lives or dies on **first-five-minutes experience**, not on feature count. A polished UI over data we already store, a trigger that watches something the visitor cares about, and a notification channel they already use — that is the demo. OAuth/MCP/agent are differentiators for *return* visits; hardening matters most once people *expose* it, which is a fast follow, not a launch blocker (and the in-proc-plugin trust boundary is already documented honestly).

---

## 2. UI 2.0 (the headline)

The current SPA is functional and form-driven; v3 makes it a product. Built entirely on data already stored — no engine changes required for the dashboards.

- **Dashboard + statistics.** Executions over time, success/failure trend, durations (p50/p95), per-workflow and per-action usage, trigger activity, recent failures. All derivable from the execution + step-execution history. A read-only stats API (aggregations) feeds it; consider a live artifact-style page that refreshes from the API.
- **Visual workflow builder, with a power-user escape hatch.** A node graph (drag-drop, connect steps, see the gate branch) becomes the friendly default — but it is **not** a full replacement. Power users keep a leaner mode: the form/list editor with direct access to each step's raw JSON config, no canvas gimmicks, fast keyboard-driven editing. Decided: ship **dual-mode** (visual default + advanced/raw view, toggle per workflow), not one or the other. The data model already supports both (ordered steps, typed configs, schema-rendered forms per node); this is front-end reshaping over the existing API, not a domain change.
- **Execution inspector upgrade.** Timeline view of a run, per-step input/output, attempt history, the gate open/closed reason, and a diff between two runs (e.g. original vs retried).
- **Global search & filtering.** Across workflows, executions (by status/workflow/date), and connections.
- **Workflow state viewer.** A per-workflow "State" tab to view (and hand-edit/clear) stored KV entries from §3, grouped by key namespace — the debugging surface for "why didn't it re-alert."
- **Templates gallery.** Generalize `showcase-everything.workflow.json` into an in-app, importable template library with one-click "use this," wired to the export/import format that already exists.
- **Onboarding & empty states.** First-run guidance, "create your first workflow," "connect a channel" — the difference between a public visitor bouncing and building.
- **Polish.** Light/dark theming, responsive layout, an accessibility pass, and **Playwright E2E** tests for the critical paths (these become the front-end equivalent of the backend's tests-first rule).

Open question to resolve before building: graph library choice (React Flow is the obvious candidate). The replacement-vs-alternate question is settled — dual-mode (visual default + advanced/raw view).

---

## 3. Triggers & durable state (the new primitive)

This is the only part of the MVP that touches the engine, and it's the one that changes what AutomateX *is*: from "runs when called" to "watches and remembers."

- **Generic polling trigger (`http.poll`)** and an **RSS/Atom trigger** — the broadly useful shapes behind countless "watch X for changes" automations.
- **Persisted dedup / "last-seen" state.** Today `matrix.onMessage` keeps an in-memory since-token, so a restart could replay or miss. Feed triggers need **durable** memory: a small state store (a `TriggerState` table keyed by trigger id + a state key, or a general per-workflow KV) so "have I already alerted on this item" survives restarts and recreation. This is the genuinely new core primitive; everything else here composes on top of it.
- **Per-workflow KV state store — one flexible primitive, scoped by key convention.** Generalize "remember between runs" (counters, cursors, last-seen ids) into a single `WorkflowState(workflowId, key, value)` store: workflow-*owned* (cascades on delete, workspace-isolated) but with a **free-form key** so lower scopes are expressed by convention (`trigger:<id>:seen:<itemId>`, `action:<n>:cursor`) rather than parallel trigger/action/workflow state tables. Decided: **one dumb KV, infinite scopes** — not three state types. Values are **plaintext JSON, not encrypted** (state is general-purpose and viewable; secrets stay in connections, which are encrypted + masked). The store carries one cheap built-in — an optional per-entry **TTL/expiry** for "max age" retention — and nothing more: dedup *rules* (uniqueness, window, max-items) live on the **consuming trigger's config**, not in the KV table, so the foundation stays a small testable primitive and never becomes a configurable-rules engine in the bedrock. Exposed to plugins via `TriggerContext`/`ActionContext` (the feed triggers are plugins and need it); exact SDK surface decided when the first consumer lands.
- **Webhook payload → templating.** Close the recorded v1 gap: webhook bodies are currently ignored. Make the inbound payload available as `{{trigger.payload.…}}` like every other trigger, so webhook-driven workflows can actually use what they receive.

Design rule (tests-first): dedup is **exactly-once-ish by item identity** — a feed item's stable id (guid/link/hash) recorded after a successful fire; the trigger never re-fires a recorded id; a restart mid-batch may re-fire an *un*recorded item (at-least-once on crash, never duplicate on restart). Pin this in tests before implementing, same as the gate semantics.

---

## 4. Notifications & integration breadth

Three first-party plugins beside the existing `matrix.send`, chosen (June 2026) for reach and triviality:

- **Discord (`discord.send`).** Incoming-webhook based — a Discord webhook URL is the entire connection; post content/embeds. The lowest-friction "it pinged my phone" channel for personal and community setups.
- **Email (`email.send`, SMTP).** An SMTP connection (host/port/username/password/from-address, TLS), send to/subject/body. Universal; the right default for alerts and digests.
- **Pushover (`pushover.send`).** App token + user/group key connection, POST to the Pushover messages API (title/message/priority/sound). Purpose-built mobile push — the best fit for the hockey-alert "buzz my phone the instant it happens" goal.

All follow the established plugin pattern: `[Action]` + `IAction<,>`, a `[ConnectionType]` for guided setup, secrets write-only + encrypted, behavioral tests first (mirroring the `matrix.send` / `llm.prompt` test style). They ride the catalog + hot-reload machinery already in place.

---

## 5. The dogfood recipe — hockey alerts

The personal success criterion, and the demo that proves §3 + §4 together:

```
feed/poll trigger (NHL transactions + Oilers news, Storhamar news)
  → llm.prompt  ("is this a trade rumor, trade, or signing involving <team>? one word")
  → gate        (isTruthy)
  → discord.send / email.send   ("🏒 Oilers: <headline> — <link>")
```

The LLM classify, `gate`, and notify already exist; the durable feed trigger + dedup state (§3) is the missing piece. **First task of this epic is a source-scouting spike** — verify exactly what feeds/APIs exist before designing. Expected shape: the NHL side has clean machine-readable transaction data (an `nhl` plugin pulling Oilers transactions/signings is realistic); the **Storhamar / Norwegian league** side has far sparser structured data, so it leans on RSS + news-site polling with the LLM filter doing the heavy lifting. Scope note: this watches **news and official transactions only** — informational alerts, nothing resembling a trading signal.

---

## 6. Release engineering & docs (because it goes public)

Going public is its own work, not a toggle:

- **Repo hygiene:** `SECURITY.md` (the in-proc-plugin trust boundary stated plainly), `CONTRIBUTING.md`, license clarity, issue/PR templates, a clean README front door with screenshots/GIF of the new UI.
- **Docs:** a getting-started path ("`docker compose up` → first workflow in 10 minutes"), the recipes index, and a **plugin-dev guide** (the SDK is the differentiator — make authoring obvious).
- **Observability surfacing:** OpenTelemetry is already wired in ServiceDefaults — expose a metrics/health view (and optionally a Prometheus endpoint) so self-hosters can see their instance.
- **API stability:** a versioned, documented API surface so the public can build against it without churn.
- **Pre-public security pass (lightweight):** the *launch-blocking* subset only — confirm secrets never log, the auth gate covers everything, dependency/CVE scan in CI. The heavier hardening set is §7, a fast follow.

---

## 7. Deferred to v3.x (sequenced, not cut)

Recorded so they don't evaporate; each has a home in the v2 plan doc and grows from a seam that already exists. These all ship within the **v3.\*** cycle — each can be its own point release or a few bundled together, whatever sequences best at the time; none are dropped past v3.

- **OAuth connection types + field provenance** (v2-plan §"Connection-types follow-ups") — plugin declares authorize/token endpoints + scopes; engine runs the flow + refresh and populates **system-managed read-only** fields. Interactive, stateful, adds a callback surface. Grows from `IConnectionType`. *Strong v3.1 candidate.*
- **MCP both directions** (v2-plan "orchestration engine, not agent runtime") — AutomateX as MCP **server** (workflows become tools for Claude/agents) + an **`mcp.call` action** (the MCP tool ecosystem as workflow steps). The proper home for "AutomateX gets smart."
- **Bounded `llm.agent` plugin** — max iterations, curated tools, cost ceiling; agentic reasoning at the edges, never in the deterministic core.
- **Branching beyond `gate`** — multi-path / try-catch error branches (the gate is the linear-halt primitive; this is the fork).
- **Workflow-scoped connections** — nullable `WorkflowId` on Connection, blast-radius reduction.
- **`prompt=none` idle re-auth** — seamless idle-expiry redirect (refresh tokens already cover the access-token boundary).
- **Hardening set for exposed deployment** — out-of-proc plugin sandboxing (the real isolation boundary), audit log, rate limiting, instance-admin role, envelope encryption / per-tenant DEKs (the `v1:`/`v2:` ciphertext prefix is ready), action idempotency keys.
  - **Note — sandboxing retires the in-proc plugin-loading workarounds.** Several fragilities exist *only* because plugins share the host's CLR: the `PluginLoadContext` name rules (defer SDK/`Microsoft.Extensions.*` to the host so Types unify; resolver-decides for bundled deps), and `PluginReflection.LoadableTypes` (tolerate a plugin's partial type-load failure so it can't abort discovery). Out-of-proc dissolves this whole class — each plugin loads in its own process with its own dependency closure behind a serialized boundary, so type-unity and "one bad plugin aborts the scan" simply don't exist. When sandboxing lands, delete these workarounds rather than carry them. They are load-bearing only for the in-proc window (all of v3 MVP + launch).
- **Broaden export/import** — beyond cron-only triggers; possibly a community plugin marketplace beyond the first-party catalog.

---

## 8. Milestones — ruthlessly small (same anti-stall discipline as v2)

Each its own commit under the **v3** umbrella tag; tests/rules first; dogfooded. Proposed sequence (confirm the first one before building):

1. **v3 plan doc** (this file) — anchor the scope. *(done)*
2. **Durable state primitive** — the `TriggerState`/KV store, tests-first (dedup identity + restart semantics), no trigger consuming it yet. The foundation everything in §3 stands on.
3. **Feed/poll triggers** — `http.poll` + RSS trigger built on the state store; webhook payload → templating alongside.
4. **Source-scouting spike + hockey plugins** — verify NHL/Storhamar sources, then the `nhl` plugin (+ RSS config for Storhamar). First real consumer of the new triggers.
5. **Notification plugins** — `discord.send` + `email.send` + `pushover.send`, tests-first.
6. **Hockey workflow end-to-end** — the §5 recipe, dogfooded for real.
7. **UI 2.0** — likely sub-milestoned: stats API + dashboard → execution inspector → visual builder → templates/onboarding → theming/a11y/E2E.
8. **Release engineering** — docs, SECURITY/CONTRIBUTING, screenshots, observability view, API-stability pass.
9. **Tag v3, make the repo public.**

Sequencing note: state → triggers → hockey gives an early, personally motivating end-to-end win that exercises the one risky new primitive before the large UI investment. UI 2.0 is the biggest single block and benefits from having real, varied execution history (which steps 2–6 generate) to make the dashboards look alive.
