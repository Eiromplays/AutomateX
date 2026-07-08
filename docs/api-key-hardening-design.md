# v4.11 — per-key rate limits + IP allowlists (design)

Closes the API-key follow-up deferred at v4.6 (`auth-keys-design.md:150`, "the entity has room to add
later"): let each dynamic key carry an optional **requests-per-minute** cap and an optional **client-IP
allowlist**, both enforced at the auth boundary. Single track, engine-first (tests-first), auth-sensitive
— the two enforcement slices get a subagent review, like v4.9 m2.

## What & why

Dynamic keys (v4.6) are bounded by *scope*, *role*, and *expiry*, but not by *how fast* or *from where*
they're used. A leaked `Instance` key can be hammered from anywhere until it's noticed and revoked. Two
per-key controls close that:

- **Rate limit.** A per-key requests/minute cap so one key can't flood the API. The infra already exists
  — `RateLimitingExtensions` uses ASP.NET's `AddRateLimiter` with per-IP fixed-window policies for the
  public webhook/auth endpoints. This adds a **key-partitioned** limiter over the gated `/api` surface.
- **IP allowlist.** A per-key set of CIDRs/IPs; a request presenting the key from any other client IP is
  refused at the gate. The real client IP is already recovered from the trusted proxy by
  `UseForwardedHeaders` (v4.6), so the check can't be spoofed by a raw `X-Forwarded-For`.

Both are **opt-in and null/empty = today's behaviour**, so no existing key changes.

## Shape

- **`ApiKey` gains two columns.** `RateLimitPerMinute` (`int?`, null = unlimited) and `IpAllowlist`
  (`text[]`, empty = unrestricted — CIDRs and/or bare IPs, e.g. `["203.0.113.0/24", "198.51.100.7"]`).
  Set at mint time; a migration adds both (nullable/empty defaults, so existing rows are unaffected).
- **`ApiKeyContext` carries them forward.** `ApiKeyResolver` already builds the context on every
  authenticated request; it adds `RateLimitPerMinute` + the parsed allowlist so the gate and the limiter
  read them without a second DB hit.
- **IP allowlist enforced in `AuthGateMiddleware`.** Right after a key resolves (where `ApiKeyContext.Set`
  is called), if the key has a non-empty allowlist and the connection's `RemoteIpAddress` isn't in it →
  **403** (a distinct message from the 401 "no credential"). Covers `/api` and `/hubs` (both gated).
  Matching reuses the CIDR parsing from the v4.8 egress allowlist (`EgressPolicy` in
  `AutomateX.Plugin.Protocol`) via a small `ClientIpAllowlist` value type (`Parse(entries)` →
  `Allows(IPAddress)`). Malformed entries are **rejected at mint (400)**, never silently dropped — for a
  restricting control, dropping a typo'd CIDR could leave the list empty (= unrestricted).
- **Rate limit via a global limiter.** A `GlobalLimiter` partitioner (`RateLimitingExtensions`) inspects
  the already-set `ApiKeyContext`: a key with a limit → a fixed-window partition keyed by the **key id**
  with that limit; no key or no limit → `NoLimiter` (pass-through). The limiter middleware runs after the
  gate (`Program.cs`: `UseAutomateXAuth` → `UseRateLimiter`), so the context is present. Rejections return
  **429** (the limiter's existing status). The per-IP webhook/auth policies are untouched.
- **Mint + manage surface.** The create-key request/endpoint accepts `rateLimitPerMinute?` and
  `ipAllowlist?`; the response and list show them (the secret is still shown once). Frontend: the
  **Settings → API keys** create form gets a number field + an allowlist entries field, and the list
  renders both.

## Slice plan

**m1 — data model + context plumbing (backend, tests-first, migration).**
Add the two `ApiKey` columns + `AddApiKeyGuards` migration; thread `RateLimitPerMinute` + parsed
`IpAllowlist` through `ApiKeyContext` and `ApiKeyResolver`; accept them in the mint path. No enforcement
yet. Tests: a key minted with a limit + allowlist round-trips into its resolved `ApiKeyContext`; a key
minted without either resolves to today's context (null/empty). Malformed allowlist entries are rejected
at mint.
*Verify:* `dotnet test`; Eirik runs the migration.

**m2 — IP allowlist enforcement at the gate (auth-sensitive).**
`AuthGateMiddleware` refuses (403) a keyed request whose client IP isn't in the key's non-empty allowlist;
empty allowlist = unrestricted. Add the `ClientIpAllowlist` matcher (CIDR + bare IP). Tests: a listed IP
(and a CIDR member) passes; an unlisted IP gets 403; an empty allowlist is unrestricted; the check uses
the forwarded (real) client IP. **Subagent auth review** of the gate diff.
*Verify:* `dotnet test`; a non-listed IP can't use the key; a listed one can.

**m3 — per-key rate limit (backend).**
Add the key-partitioned `GlobalLimiter` to `RateLimitingExtensions`; pass-through when there's no key or
no limit. Tests: a key over its per-minute limit gets 429; an unlimited key is unaffected; two keys with
separate limits don't share a bucket; unauthenticated/other endpoints are unaffected. **Subagent review**
(shares the auth surface).
*Verify:* `dotnet test`.

**m4 — mint + manage UI (frontend).**
Create-key form gains a rate-limit number field and an IP-allowlist entries field; the keys list shows
both. Client-side validation mirrors the server (positive int; CIDR/IP shape).
*Verify:* `pnpm test/typecheck/lint/build`.

**m5 — docs + `CHANGELOG` v4.11.0 (no code).**
`CHANGELOG` v4.11.0 entry; extend `docs/recipes/api-keys.md` (set a limit / lock a key to an IP range);
clear the `auth-keys-design.md:150` deferred line. Final verify: `dotnet test` + `pnpm` gate green, diff
review.

## Calls made

- **Enforce at the gate + a global limiter, not per-endpoint attributes.** The gate already owns key
  resolution and runs before the limiter, so IP-allowlist (403) and the key-partitioned limiter (429)
  slot in with no per-endpoint plumbing and cover the whole gated surface uniformly.
- **Fixed-window, keyed by key id.** Matches the existing webhook/auth limiters (same primitive, one
  window). Sliding/token-bucket is a later refinement if needed; fixed-window is the proven, simple
  choice here.
- **IP is the forwarded client IP.** Reuses the v4.6 trusted-proxy `UseForwardedHeaders` pipeline, so an
  untrusted caller can't rotate it. The allowlist is CIDR/IP only (no hostnames — a client presents an
  address, not a name), a strict subset of the egress-allowlist matcher.
- **Opt-in, null/empty = unchanged.** No existing key is affected; both fields default off.
- **One migration** (`AddApiKeyGuards`: two nullable/empty columns). Eirik runs it.
- **Commit scopes:** m1–m3 `feat(apikeys): … (v4.11 mN)`; m4 `feat(web): … (v4.11 m4)`; m5
  `docs(…): … (v4.11 m5)`.

## Deferred (still)

The **read-only-across-all-workspaces Instance key** variant (`auth-keys-design.md:148`) stays deferred —
unrelated to hardening, and a clean no-migration add whenever it's wanted.
