# AutomateX v4.9.0

A loose-ends release: plugin logs now tail live, and the UI gets one consistent icon set — clearing the
two follow-ups that have ridden along since v4.4/v4.5. Two independent subsystems, no migration.

## Highlights

- **Plugin logs tail live.** The plugin **Logs** console read its output only through the
  `GET /plugins/{name}/logs?since=` poll, so a burst of output showed up a poll-interval late and a live
  tail flickered. Lines are now pushed as they're appended, over a dedicated **`PluginLogsHub`**
  (`/hubs/plugin-logs`) — kept separate from the workspace execution hub because the auth boundary
  differs: `JoinPlugin` requires **instance-admin** (mirroring the logs endpoint; logs can carry
  sensitive runtime data), not workspace membership. The stream is **additive** — the console snapshots
  via the existing poll, then tails by `Seq`, with the poll left on as the reconnect backstop. The engine
  gains a name-agnostic `IPluginLogSink` seam (a no-op by default, so it keeps no SignalR dependency); the
  Web layer resolves the plugin dll to its global name and broadcasts to `plugin:{name}`.
- **An app-wide icon pass.** The UI leaned on ad-hoc emoji and text glyphs (🔗 ⛓ ✕, status dots, text-only
  nav) that render inconsistently across platforms and don't theme or scale. They're replaced by
  [`lucide-react`](https://lucide.dev) — a tree-shakeable, MIT icon set that pairs with the React +
  Tailwind + Radix stack — across the nav, the execution and plugin **status chips**, the executions
  **relationship badges** (chained / retry), and `schema-form`'s inline builder controls. Purely
  presentational: sizes and colours come from the Tailwind classes already in use, no layout change, and
  icon-only controls keep an accessible label.

## Upgrade notes

- **No migration, no new entity, no config key.** Track A is a runtime stream over existing data; Track B
  is frontend only.
- **Frontend dependency:** the icon pass adds `lucide-react` — run `pnpm install` before building the web
  app.
- **Nothing regresses if the socket drops.** The poll endpoint stays the snapshot + backstop, and the
  500-line ring guarantees a dropped best-effort line reappears on the next snapshot, so the stream never
  has to be reliable. The poll endpoint and the execution hub are unchanged.

## Known follow-ups

- Both long-standing follow-ups are now closed: the SignalR live-log stream and the app-wide icon pass
  (both this release).
- Per-line SignalR sends match the ring's per-line append; batching under very chatty plugins is a
  possible later optimisation, not needed now.

See the [live plugin-log stream + icon pass design](docs/plugin-log-stream-and-icons-design.md) and the
[plugin operations recipe](docs/recipes/plugin-operations.md). Full history: [CHANGELOG.md](CHANGELOG.md).
