# AutomateX v4.11.0

A focused minor hardening dynamic API keys: each key can now carry a **requests-per-minute cap** and a
**client-IP allowlist**, both enforced at the auth boundary. One migration (`AddApiKeyGuards`); opt-in,
so existing keys are unchanged.

## Highlights

- **Per-key rate limit.** A key can set `rateLimitPerMinute`; requests presenting it are fixed-windowed
  (1 minute) and get **429** over the cap. A global limiter reads the authenticated key from the request
  — no per-endpoint wiring — bucketed by the key's **id** (not its name, which isn't unique), so two keys
  never share an allowance. Any request with no key, or a key with no limit, passes through untouched, so
  the existing per-IP webhook/auth limiters are unaffected.
- **Per-key IP allowlist.** A key can set `ipAllowlist` — CIDRs and/or bare IPs; a request presenting it
  from any other client IP is refused with **403**, on both `/api` and the live `/hubs`. The match uses
  the real client IP recovered from the trusted proxy (the v4.6 forwarded-headers pipeline), so it can't
  be spoofed with a raw `X-Forwarded-For`. Malformed entries are rejected when the key is minted (400),
  and parsing fails **closed** — a stored entry that won't parse fences the key rather than silently
  opening it up.

## Upgrade notes

- **One migration.** `AddApiKeyGuards` adds `ApiKeys.RateLimitPerMinute` (nullable int) and
  `ApiKeys.IpAllowlist` (`text[]`, defaulting to `'{}'` so existing rows backfill to "any address"). It
  **also carries the v4.10 forEach schema** (the `ForEachItemResults` table and the `ForEachState` column
  changes), which shipped without its own migration in v4.10 and folded in here. Applied automatically on
  startup (`Database:MigrateOnStartup`, default on) or via `dotnet ef database update`.
- **Both default off.** A null limit and an empty allowlist are byte-for-byte the previous behaviour; the
  guards are opt-in per key.
- **Behind a reverse proxy, pin `ForwardedHeaders:KnownProxies`** to your proxy's IP(s). The allowlist
  trusts the client IP recovered from the proxy's forwarded headers; if the trusted-proxy set is too
  broad, a host that can reach the app directly could forge that header. Pinning the proxy makes the
  client IP trustworthy.
- Set the guards in **Settings → API keys** or via `POST /api/keys` (`rateLimitPerMinute`, `ipAllowlist`).

## Known follow-ups

- The per-key rate-limits / IP-allowlists follow-up (deferred at v4.6) is now closed.
- Still open: a **read-only-across-all-workspaces `Instance` key** variant — a clean no-migration add
  (the `Role` column already exists) whenever it's wanted.

See the [API-key hardening design](docs/api-key-hardening-design.md) and the
[API keys recipe](docs/recipes/api-keys.md). Full history: [CHANGELOG.md](CHANGELOG.md).
