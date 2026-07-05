# v4.8 — egress allowlist + builder headers editor (design)

A deliberate **loose-ends release** that clears the two remaining follow-ups from the v4.5.2 / v4.7 arc.
They're unrelated subsystems, so they ride as **two independent slice tracks** with their own gates:

- **Track A — Egress allowlist (backend).** Turn `Engine:BlockPrivateNetworkRequests` from all-or-nothing
  into a policy: permit named hosts/CIDRs while the guard is on. Continues the v4.7 egress work.
- **Track B — Builder headers editor (frontend).** A first-class key/value editor for string-map config
  fields (`http.poll` headers, and `http.request`/`webhook.send` headers) instead of the raw-JSON
  fallback. Closes the v4.5.2 "trigger editor can't edit `http.poll` headers" follow-up.

Neither track touches the other's files; either can be built first. **No migration in the whole release.**

## Track A — Egress allowlist

### What & why

v4.7 made `Engine:BlockPrivateNetworkRequests` reach across the plugin boundary, but it's still binary:
on = every private/loopback/link-local target is blocked. An internet-exposed instance that *does*
legitimately call one internal host (a LAN webhook, a self-hosted API) can't turn the guard on without
losing that call. The allowlist is the escape hatch: guard on, plus an explicit set of permitted
hosts/CIDRs.

### Shape

- **`EngineOptions.EgressAllowlist`** — `string[]` of hostnames and/or CIDRs
  (e.g. `["metrics.lan", "10.8.0.0/24", "fd00::/8"]`). Empty (default) = exactly v4.7 behavior.
- **`EgressPolicy`** (new, in `AutomateX.Plugin.Protocol` next to `EgressGuard`) — a parsed policy:
  `bool BlockPrivate`, a set of allow-hostnames, and a list of allow-`IPNetwork`s. Parsed once from the
  raw strings; a malformed entry is dropped with a warning, never fails startup.
- **`EgressGuard` grows a policy-aware path.** The static `FilteringConnectCallback` becomes a factory
  `MakeConnectCallback(EgressPolicy)` that closes over the policy; the connect is permitted if the target
  host matches an allow-hostname **or** the dialed IP falls in an allow-`IPNetwork`, else the existing
  block rule applies. `GuardAsync` takes the policy too. With an empty allowlist the behavior is
  identical to v4.7 (the classifier is unchanged; the allowlist only ever *widens*).
- **Transport to the plugin host** extends the v4.7 env: alongside `AUTOMATEX_EGRESS_BLOCK_PRIVATE`, add
  `AUTOMATEX_EGRESS_ALLOWLIST` (comma-separated). The host builds an `EgressPolicy` from both and calls
  `MakeConnectCallback`. Threading mirrors v4.7 m3: `EngineOptions` → `PluginProcessSupervisor` →
  `PluginClient` → child env.

### Slice plan (Track A)

**m1 — `EgressPolicy` + allowlist matching in `EgressGuard` (backend, tests-first).**
Add `EgressPolicy` (parse hostnames + CIDRs; drop-and-warn on malformed). Replace the static
`FilteringConnectCallback` with `MakeConnectCallback(policy)`; add policy to `GuardAsync`. Engine
`GuardedHandler` builds the policy from `EngineOptions` (block flag + allowlist). Unit-test the pure
matcher: allow-hostname permits an otherwise-blocked host; allow-CIDR (v4 + v6) permits an IP in range;
a non-matching private target is still blocked; **empty allowlist reproduces the v4.7 table exactly**
(extend `EgressGuardTests`). No migration.
*Verify:* `dotnet test`; the v4.7 classification table still passes with an empty policy.

**m2 — `PluginHost` honors the allowlist (out-of-proc, tests-first).**
Add the `AUTOMATEX_EGRESS_ALLOWLIST` env; the host builds `EgressPolicy` from both env vars and uses
`MakeConnectCallback`. OOP test (extends the v4.7 loopback test): with the guard on **and** the loopback
host allowlisted, `sample.httpget` reaches it (200); with the guard on and no allowlist, it's blocked.
*Verify:* the OOP test proves the allowlist widens the guard across the boundary.

**m3 — Wire `EgressAllowlist` through the supervisor (tests-first).**
Thread `EngineOptions.EgressAllowlist` → `PluginProcessSupervisor` → `PluginClient` → child env
(extends the v4.7 m3 path; the supervisor/client gain an allowlist param next to `blockPrivateEgress`).
Supervisor test: an allowlisted private address is reachable end-to-end with the guard on.
*Verify:* end-to-end allow through the full supervisor path.

## Track B — Builder headers editor

### What & why

`http.poll`'s config already carries `Headers` (`Dictionary<string,string>`), and the builder *does*
surface it — but only via the generic `json` fallback in `SchemaForm`: a raw `<textarea>` where you
hand-type `{"Authorization":"Bearer …"}`, and any invalid-JSON keystroke is silently dropped
(`JSON.parse` in a try/catch). That's the v4.5.2 "can't edit `http.poll` headers from the UI" — there's
no first-class editor. The same shape backs `http.request`/`webhook.send` action headers, so the fix is
general, not `http.poll`-specific.

### Shape

- **Backend already emits the signal.** `SchemaExport` serializes `Dictionary<string,string>` as
  `{"type":["object","null"],"additionalProperties":{"type":"string"}}` — a detectable string-map. No
  backend change.
- **New `"map"` field kind** (`schema-fields.ts`): `fieldKind` returns `"map"` when the schema is an
  `object` with `additionalProperties.type === "string"` and no named `properties`. Extend the
  `JsonSchema` type with `additionalProperties`. This is pure + unit-tested (`schema-fields.test.ts`).
- **`KeyValueEditor`** (`schema-form.tsx`): renders the map as add/remove rows (key input + value
  input), writing back a `Record<string,string>`. The value input reuses the existing
  reference/connection inserter, so `{{connections.*}}` still works in a header value. `SchemaForm`
  dispatches `kind === "map"` to it, replacing the raw-JSON textarea for these fields. Every other field
  is untouched.

### Slice plan (Track B)

**m4 — String-map "Headers" editor in the builder (frontend, tests-first).**
Add the `"map"` `FieldKind` + detection and extend `schema-fields.test.ts` first; then the
`KeyValueEditor` and its `SchemaForm` wiring. `http.poll`, `http.request`, and `webhook.send` headers
now render as key/value rows. Nothing server-side changes.
*Verify:* `pnpm test && pnpm typecheck && pnpm lint && pnpm build`; the `fieldKind` test covers
map-vs-json detection; manual check that a saved header round-trips and a `{{connections.*}}` value
inserts.

## Calls made (both tracks)

- **Allowlist widens, never narrows.** It can only *permit* otherwise-blocked targets; it never blocks
  something the base rule allows. The classifier (`IsBlockedAddress`) is unchanged, so the v4.7 behavior
  is the empty-allowlist special case — no regression risk to existing deployments.
- **Allowlist entries are hostnames or CIDRs.** A hostname match is checked against the request's host
  (pre-resolution, so an allowed internal name works even though it resolves private); a CIDR is checked
  against the dialed IP (rebinding-proof, same as the block path). Malformed entries are dropped with a
  warning, never fatal.
- **Policy still read at child launch, not hot-reloaded** — same as v4.7 (`BindConfiguration`, restart to
  change; reload recycles hosts). The allowlist rides the same env transport.
- **One shared implementation in `Plugin.Protocol`** — `EgressPolicy` + `EgressGuard` stay the single
  source of truth for engine and host.
- **The headers editor is schema-driven and general**, not an `http.poll` special-case: any string-map
  config field gets it. Consistent with how the builder already special-cases `switch` via `actionType`,
  but here it's keyed off the schema shape so plugins get it for free.
- **No migration, no new entity.** Track A adds config only; Track B is frontend only.
- **Commit scopes:** Track A `feat(plugins): … (v4.8 mN)`; Track B `feat(web): … (v4.8 mN)`; final
  wrap-up `docs(...): … (v4.8 mN)`.

## Release wrap-up

**m5 — Docs + `CHANGELOG` v4.8.0 (no code).**
`CHANGELOG` v4.8.0 entry (both tracks); `.env.example` gains `EGRESS_ALLOWLIST=`; recipe note on the
allowlist (extend the egress note from v4.7) and the headers editor. Clear the remaining v4.5.2
follow-up line (`http.poll` headers). Final verify: `dotnet test` + `pnpm` gate green, diff review.
