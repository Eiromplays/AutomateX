# AutomateX v4.5.2

The second half of the self-deploy fix: out-of-process plugin HTTP now matches in-process behaviour.

## Fixes

- **Default User-Agent for plugin HTTP.** In-process plugins used a shared `HttpClient` configured with
  `User-Agent: AutomateX/2.0`. The v4.0 out-of-process host gave plugins a bare `new HttpClient()` with
  no User-Agent — and `HttpClient` sends none by default. Some APIs reject that outright: GitHub returns
  **403**, so `http.poll` against `api.github.com` (the self-deploy trigger) failed on every poll,
  silently (a non-2xx never fires and never errors). The plugin host now defaults `AutomateX/2.0` on the
  trigger, action, and connection-test clients. A `User-Agent` set in a trigger/action config still
  wins — request headers take precedence over the client default.
- **Visibility: `http.poll` logs non-2xx.** The poll still ignores non-2xx (no fire, baseline
  untouched), but now logs `http.poll <url> returned <status>` to the plugin log ring, so a persistent
  401/403 is visible in the Plugins → Logs console instead of looking like silence.

## Upgrade notes

- No migration, no config changes. After upgrading, `http.poll` triggers against UA-strict APIs work
  without a manual `User-Agent` header.
- If you added a `User-Agent` header manually to work around this, you can leave it — it overrides the
  default harmlessly.

## Known follow-ups

- Out-of-process plugin HTTP clients don't yet wrap the in-process SSRF/egress guard (`GuardedHandler`)
  — tracked separately.
- The trigger editor can't edit `http.poll` headers from the UI yet.

See the [self-deploy recipe](docs/recipes/self-deploy.md). Full history: [CHANGELOG.md](CHANGELOG.md).
