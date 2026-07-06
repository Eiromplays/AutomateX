# AutomateX v4.4.0

Plugin operations: see, control, and cap the out-of-process plugins.

## Highlights

- **Observability.** The Plugins page now shows each plugin's live status (running / crashed / idle /
  disabled, uptime, restart count) and memory, plus an expandable **console** that tails the plugin's
  log output (bounded ring buffer, polled). Status/usage is visible to members; logs are instance-admin
  only.
- **Controls (instance-admin).** **Restart** a plugin's host process, and **disable/enable** it — a
  disabled plugin's actions, triggers, and connection types drop out of the builder and running
  workflows until re-enabled. Disable is a filesystem marker, so it survives restarts.
- **Resource limits (opt-in).** Under `Engine__PluginLimits`: `CallTimeout` (cancel a long action),
  `MaxMemoryBytes` (a watchdog kills + restarts an over-budget host), and `MaxConcurrentCalls` (bound
  concurrent calls to one host). All default off.

## Upgrade notes

- **No migration.** Disable state is a `.disabled` marker in the plugin folder; limits are config only.
- New endpoints under `/plugins/{name}`: `status`, `logs`, `restart`, `enabled`.
- Limits are unset by default, so existing plugins run exactly as before until you opt in.

See the [plugin operations recipe](docs/recipes/plugin-operations.md) and
[design note](docs/plugin-operations-design.md). Full history: [CHANGELOG.md](CHANGELOG.md).

---

*A live SignalR log stream (complementing the polled console) and an app-wide icon pass were tracked as
follow-ups — both closed in v4.9.0. Next major theme: AutomateX-as-MCP-server.*
