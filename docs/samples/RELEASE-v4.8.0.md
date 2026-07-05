# AutomateX v4.8.0

A loose-ends release: the egress guard gets an allowlist, and the builder gets a real headers editor —
closing the two follow-ups from the v4.7 and v4.5.2 arc. Two independent subsystems, no migration.

## Highlights

- **The egress guard gains an allowlist.** `Engine:BlockPrivateNetworkRequests` was all-or-nothing —
  on meant *every* private/loopback/link-local target was blocked, so an internet-exposed instance that
  legitimately calls one internal host (a LAN webhook, a self-hosted API) couldn't turn it on without
  losing that call. `Engine:EgressAllowlist` is the escape hatch: hostnames and/or CIDRs (e.g.
  `metrics.lan`, `10.8.0.0/24`, `fd00::/8`) that stay reachable while the guard is on. It **widens
  only** — it can permit an otherwise-blocked target but never blocks one the base rule allows, so an
  empty allowlist is exactly the v4.7 behaviour. A hostname is matched against the request host (an
  internal name works even though it resolves private); a CIDR is matched against the IP actually dialed
  (rebinding-proof). The same policy applies in-process and across the out-of-process plugin boundary.
- **A first-class headers editor in the builder.** `http.poll`, `http.request`, and `webhook.send`
  headers used to surface only through the raw-JSON textarea — you hand-typed `{"Authorization":"…"}`
  and any invalid-JSON keystroke was silently dropped. Headers now render as add/remove key/value rows,
  and the value input keeps the reference inserter so `{{connections.*}}` and step references still work
  in a header value. It's schema-driven, not `http.poll`-specific: any config field the exporter emits
  as a string→string map gets the editor, so plugins get it for free.

## Upgrade notes

- **No migration, no new entity.** Track A is config only; Track B is frontend only.
- **Config:** set the allowlist with the comma-separated `EGRESS_ALLOWLIST` env (expanded into the array
  by the app) or an `Engine:EgressAllowlist` array in appsettings. Only meaningful when the guard is on;
  empty keeps it all-or-nothing. Malformed entries are dropped with a logged warning, never fatal. Read
  at child launch, not hot-reloaded — restart to change.
- **No behaviour change unless you opt in.** With an empty allowlist the guard classifies exactly as it
  did in v4.7; the headers editor replaces the JSON textarea for these fields but round-trips the same
  saved config.

## Known follow-ups

- Both prior follow-ups are now closed: the out-of-process egress bypass (v4.7) and the `http.poll`
  headers editor (this release).

See the [egress allowlist + headers design](docs/egress-allowlist-and-poll-headers-design.md), the
[self-deploy](docs/recipes/self-deploy.md) and [transform + webhooks](docs/recipes/transform-and-webhooks.md)
recipes. Full history: [CHANGELOG.md](CHANGELOG.md).
