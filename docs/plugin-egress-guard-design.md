# Plugin egress guard (v4.7) — design

Extend the SSRF/egress guard across the out-of-process plugin boundary, so a plugin's HTTP calls obey
the same `Engine:BlockPrivateNetworkRequests` policy as the engine's own `http.request`/`webhook.send`.
Closes the v4.5.2 known follow-up ("out-of-process plugin HTTP clients still bypass the in-process
SSRF/egress guard (`GuardedHandler`)").

## What & why

Today the guard only protects **in-proc engine** HTTP. `AutomateXEngine.GuardedHandler` wires
`SsrfGuard.FilteringConnectCallback` onto the action + trigger `HttpClient`s when
`Engine:BlockPrivateNetworkRequests` is on, and `HttpRequestAction`/`WebhookSendAction` also pre-check
via `SsrfGuard.GuardAsync`. But every **plugin** runs out-of-process (v4.0), and
`AutomateX.PluginHost.Program.CreateHttpClient` hands the plugin a bare `HttpClient` with **no guard**.

So on an internet-exposed instance with the guard on, `http.request` to `169.254.169.254` (cloud
metadata) or a LAN host is blocked — but a plugin action, trigger listener, or connection test making
the same call is **not**. That's the hole: the strongest isolation boundary in the product (a plugin in
its own process) is also the one place the egress policy doesn't reach.

**What changes for the operator:** with `Engine:BlockPrivateNetworkRequests=true`, plugin HTTP is
guarded identically to engine HTTP — same block rule, same Tailscale/CGNAT carve-out. Default off is
unchanged, so existing self-hosted setups reaching LAN/Tailscale targets keep working. No new config
key, no migration.

## Shape

**One shared implementation, two call sites.** The guard is pure `System.Net.*` and must be *byte-for-
byte identical* on both sides of the pipe, so it moves to the assembly both already share.

- **`AutomateX.Plugin.Protocol` gains `EgressGuard`** (the shared host↔engine runtime — already
  referenced by the engine, the `PluginHost`, and the SDK). It holds the relocated `IsBlockedAddress`,
  `FilteringConnectCallback`, and `GuardAsync` — verbatim logic from today's `SsrfGuard`, including the
  100.64.0.0/10 CGNAT/Tailscale allowance and the IPv4-mapped-IPv6 unwrap.
- **Engine side unchanged in behavior.** `GuardedHandler`, `HttpRequestAction`, `WebhookSendAction`
  call `EgressGuard` instead of `SsrfGuard`. `SsrfGuard` is deleted (its three references are the only
  callers); the in-proc code path is otherwise identical.
- **Host side, new.** `PluginHost.CreateHttpClient` reads the policy once at process start and, when on,
  builds the client on `new SocketsHttpHandler { ConnectCallback = EgressGuard.FilteringConnectCallback }`
  — exactly mirroring `GuardedHandler`. This covers all three plugin HTTP seams, since actions, trigger
  listeners, and connection tests all get their `HttpClient` from this one factory.

**Policy transport — an environment variable at launch.** The child is started by `PluginClient` via
`ProcessStartInfo("dotnet"){ ArgumentList = { "exec", hostDll, pluginDll } }`. The policy rides as an
env var, not a CLI arg (it's config, not identity; keeps `ArgumentList` = the plugin path):

```
AUTOMATEX_EGRESS_BLOCK_PRIVATE = "true" | "false"
```

**Threading the flag:** `EngineOptions.BlockPrivateNetworkRequests` →
`PluginProcessSupervisor` (new ctor param, alongside `pluginHostDll`/`limits`) →
`PluginClient` (new ctor param) → `ProcessStartInfo.Environment[...]`. `UseShellExecute=false` already
lets us set the child environment. The composition root in `AutomateXEngine` passes the option through
where it constructs the supervisor.

**Config key:** `Engine:BlockPrivateNetworkRequests` is reused as-is — no new surface, no breaking
change. The env var is an internal transport detail, not operator-facing.

## Calls made (decided up front)

- **Parity, not a superset.** v4.7 mirrors the in-proc guard exactly. **No host/CIDR allowlist** in
  this release — the in-proc guard has none, and the follow-up is "close the bypass," so parity comes
  first. An allowlist (letting the guard stay on while permitting a named internal host) is a clean
  future extension, deferred to a later minor.
- **Policy is read at child launch, not hot-reloaded.** `BlockPrivateNetworkRequests` is
  `BindConfiguration`-bound, so changing it already implies an app restart; and plugin reload recycles
  host processes, which re-reads the env. No live-toggle path is built.
- **Single source of truth in `Plugin.Protocol`.** The guard is not duplicated into the host. Protocol
  is the existing shared-runtime seam (same assembly the SDK/`Microsoft.Extensions.*` unification pins
  across the boundary), so both sides run the same code — no drift between engine and plugin behavior.
- **Rebinding-proof `ConnectCallback`, same as in-proc.** Gate the IP the socket actually dials, not a
  pre-resolved one. `GuardAsync` (pre-check) moves too, for symmetry, though the host path relies on the
  connect callback.
- **All plugin HTTP seams, including long-poll triggers.** The guard applies to action, trigger, and
  connection-test clients — the plugin equivalent of the engine's guarded trigger client. A guarded
  `SocketsHttpHandler` does not impose the resilience/timeout behavior that long-poll clients avoid, so
  matrix-style listeners are unaffected apart from the connect-time address check.
- **Naming.** The shared type is `EgressGuard` (it's an egress-policy primitive, broader than "SSRF").
  The operator-facing config name is unchanged.
- **No migration, no frontend.** Pure runtime/security; no schema and no `pnpm` surface touched.

## Slice plan

Each slice stays green (`dotnet test`; out-of-proc tests gated by `OutOfProcGate`/`AUTOMATEX_REQUIRE_OOP`).
Tests-first for the engine/host slices. **No slice adds a migration.**

**Slice 1 — Shared `EgressGuard` in `Plugin.Protocol` (pure, tests-first).**
Relocate `IsBlockedAddress` + `FilteringConnectCallback` + `GuardAsync` into
`AutomateX.Plugin.Protocol` as `EgressGuard`; delete `SsrfGuard` and repoint its three callers
(`GuardedHandler`, `HttpRequestAction`, `WebhookSendAction`). Move `SsrfGuardTests` → `EgressGuardTests`
against the relocated type (same address table). In-proc behavior byte-identical.
*Verify:* the existing classification table passes against `EgressGuard`; `dotnet test` green.

**Slice 2 — `PluginHost` enforces egress out-of-process (tests-first).**
`PluginHost.Program`: read `AUTOMATEX_EGRESS_BLOCK_PRIVATE` once at startup; `CreateHttpClient` returns
a guarded `SocketsHttpHandler`-backed client when on. Add a small drivable HTTP action to
`AutomateX.SamplePlugin` (e.g. `sample.httpget` GETting a configured URL) so the boundary is testable.
A `PluginHostSpikeTests`-style test starts the host with the env set and asserts a call to a loopback
address fails with the egress error; with the env unset the same call is permitted.
*Verify:* the OOP test proves the child blocks/permits per env.

**Slice 3 — Wire the policy from `EngineOptions` through the supervisor (tests-first).**
Thread `BlockPrivateNetworkRequests` → `PluginProcessSupervisor` → `PluginClient` →
`ProcessStartInfo.Environment`; update the `AutomateXEngine` composition root.
A supervisor/client test drives a plugin HTTP call end-to-end and asserts it's blocked when the option
is on and permitted when off.
*Verify:* with `BlockPrivateNetworkRequests=true`, a plugin action hitting a private address is blocked
through the full supervisor path.

**Slice 4 — Docs + `CHANGELOG` v4.7.0 wrap-up (no code).**
Egress recipe/note (extend the SSRF doc if present), `CHANGELOG` v4.7.0 entry, and clear the v4.5.2
known-follow-up line. Final verify: `dotnet test` green + diff review (consider a subagent pass, since
this is a security boundary).

---

**Commit scope:** `feat(plugins): … (v4.7 mN)` (the boundary is the plugin host). Slice 4 uses
`docs(plugins): … (v4.7 m4)`.
