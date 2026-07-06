# Recipe: operating plugins

Since v4.0 each plugin runs in its own process. The **Plugins** page turns that boundary into an
operations surface — see health, read logs, restart, quarantine, and cap resources.

## See status

Each plugin shows a badge — **running**, **crashed**, **idle** (not launched yet), or **disabled** —
plus memory and a restart count when relevant. This is visible to any workspace member.

## Read logs

Expand a plugin's **Logs** to tail its output. The console loads a snapshot, then **tails live** over
SignalR — new lines appear as the plugin emits them, no poll-interval lag. If the socket drops it falls
back to the couple-of-seconds poll and reconciles on reconnect, so you never miss or double a line.
Logs are **instance-admin only** (they can carry sensitive runtime data); other members see a note.

## Restart

**Restart** kills the plugin's host process; the next call relaunches it fresh. Use it after
updating a plugin, or to clear a wedged process. Instance-admin only.

## Disable / enable

**Disable** quarantines a misbehaving plugin: its actions, triggers, and connection types disappear
from the builder and running workflows until you **Enable** it again. It's a filesystem marker, so it
survives restarts. Workflows that used the plugin's actions will fail while it's disabled — the intent
is to stop a bad plugin fast. Instance-admin only.

## Resource limits (opt-in)

All off by default — set only what you need, under `Engine__PluginLimits`:

- `CallTimeout` (e.g. `00:00:30`) — cancel an action that runs longer than this.
- `MaxMemoryBytes` (e.g. `536870912` for 512 MB) — a watchdog kills + restarts a host that exceeds it.
- `MaxConcurrentCalls` (e.g. `4`) — bound how many action calls run against one plugin host at once.

A limit breach surfaces as a normal step error (timeout) or a restarted host (memory); nothing else is
affected.

## Tips

- Prefer **restart** over **disable** for a merely-stuck plugin; disable is for "this plugin is
  actively harmful, take it out of circulation."
- Set `MaxMemoryBytes` for plugins with native or leaky dependencies; leave it unset otherwise.
