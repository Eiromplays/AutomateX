# AutomateX v3.1.0

Reference steps by name, reshape data inline, and reach two more destinations — plus a modern web
toolchain.

## Highlights

- **Named step references.** Every step now has a stable `key` (slugged from its name, unique per
  version), so `{{steps.<key>.output.<field>}}` keeps working when you rename *or* reorder steps —
  no more silently re-pointed references. The positional `{{steps.<order>…}}` form still works.
  - The builder validates references inline (resolves / fragile-index / unknown step) and offers a
    one-click **Convert index refs → names**.
  - Inline autocomplete suggests upstream steps and then their output fields from each action's
    result schema; the 🔗 inserter is now a tabbed **Steps / Connections** picker.
  - Saving rejects references that can never resolve (unknown key / out-of-range order).
- **`transform` action (JMESPath).** Reshape or extract JSON between steps with a single expression
  — filters (`items[?ok].id`), multiselect hashes (`{count: length(items), ids: items[].id}`), and
  functions (`sort_by`, `length`, `join`). The result becomes the step output directly.
- **`webhook.send` action.** First-class outbound webhook: POST a templated payload with optional
  HMAC-SHA256 signing that matches AutomateX's own inbound verification
  (`X-Webhook-Signature: sha256=<hex>`, header overridable). SSRF-guarded; fails on non-2xx.
- **Slack & Telegram.** `slack.send` (incoming webhook) and `telegram.send` (Bot API; the token is
  verified via `getMe`) join `discord.send` / `pushover.send` / `matrix.send` / `email.send`.
- **React Router v8.** The web app moves to React Router 8 (Vite 8, TypeScript 6, Node baseline
  22.22). No behavioural change — SPA framework mode, so the v8 breaking changes didn't apply.

See the [transform & webhooks recipe](docs/recipes/transform-and-webhooks.md) and the
[named step references design](docs/steps-references-design.md).

## Upgrade notes

- **Migration:** this release adds `AddStepKey`, which backfills existing steps with positional keys
  (`step-1`, `step-2`, …). It applies on startup by default (`Database__MigrateOnStartup`) — back up
  Postgres first. Re-saving a workflow re-derives name-based keys for its new version.
- **New dependency:** the API now bundles `JsonCons.JmesPath` (powers `transform`); a normal
  `dotnet restore`/image rebuild pulls it in.
- Nothing else changes — existing `{{steps.<order>…}}` references and all v3.0 workflows keep working.

Full history: [CHANGELOG.md](CHANGELOG.md).
