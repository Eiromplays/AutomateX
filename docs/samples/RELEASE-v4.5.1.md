# AutomateX v4.5.1

A resilience fix for out-of-process plugin triggers — poll-based triggers (like self-deploy) no longer
die on a transient network blip.

## Fixes

- **Out-of-process trigger listeners self-heal.** Since v4.0 plugin triggers run out-of-process, but a
  listener whose `RunAsync` threw was killed and never restarted — in-process listeners already retry,
  out-of-process ones didn't. A single transient error (e.g. `Resource temporarily unavailable` while
  resolving an upstream host) could silently stop a poll trigger until the app was restarted. The
  plugin host now retries a failed listener with backoff, matching the in-process loop.
- **Listener errors are visible.** An out-of-process `trigger.error` now stamps the trigger's
  `LastError` / `LastErrorAt` (cleared on the next successful fire), instead of only landing in the
  plugin log ring — so a stalled trigger shows up in the UI.
- **Connection edits recycle dependent listeners.** Trigger `{{connections.*}}` are resolved once at
  listener start; a per-workspace connection version is folded into the listener fingerprint, so
  rotating a secret (e.g. an expired token) re-resolves running listeners without a restart.

## Upgrade notes

- No migration, no config changes.
- After upgrading, a previously stalled poll trigger recovers on its own: the next successful poll
  re-fires once (its stored content-hash baseline is stale) and reconciles to the newest state.

See the [self-deploy recipe](docs/recipes/self-deploy.md) (new troubleshooting section). Full history:
[CHANGELOG.md](CHANGELOG.md).
