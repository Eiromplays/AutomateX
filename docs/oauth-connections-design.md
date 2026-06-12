# OAuth connections — design proposal

**Status:** proposal for buy-in · **Date:** June 2026 · the v3.x "OAuth connections" arc.

## Goal

A connection whose credential is an **OAuth2 access token** obtained through the
authorization-code flow (with refresh), instead of a user-pasted key/token. Flow:
"Connect with X" → provider consent screen → tokens stored encrypted → a step using
`{{connections.<name>.accessToken}}` always gets a **fresh** token.

## Where we are (the constraint)

A `Connection` is `name + provider + EncryptedSecrets` (one AES-GCM JSON blob) in a
workspace. `IConnectionType` declares `Fields` so the UI renders a guided form for manual
entry; runtime decrypts the blob and resolves `{{connections.<name>.<field>}}`. Today every
field is **user-typed** and **static**. OAuth differs on three axes:

1. **Acquisition** — a browser redirect round-trip (authorize → callback → token exchange),
   not a form the user types.
2. **Provider metadata** — authorize URL, token URL, scopes, and an app's client id/secret.
3. **Lifecycle** — tokens expire; the access token must be refreshed before use.

The good news: storage is unchanged. The tokens (`accessToken`, `refreshToken`, `expiresAt`)
are just more fields in the same encrypted blob, addressable by the same templating.

## The model decisions (need your call)

### A. Where do the app's client id/secret + endpoints come from?

- **Recommended — BYO app, per connection.** The connection type declares the OAuth *shape*
  (authorize/token endpoints + default scopes) where it's a known provider; the user supplies
  **client id + client secret per connection** (and, for the generic type, the endpoints too).
  The redirect URI they register in their provider app is `<base>/api/connections/oauth/callback`.
  Rationale: self-hosted, no central secret store, works with any provider and any user's own
  app — the same spirit as the rest of AutomateX.
- **Alternative — instance apps via config.** The operator pre-registers one app per provider
  and puts client id/secret in config; users just click Connect. Nicer UX, but needs operator
  setup and stores app secrets centrally. Can be added later on top of (A).

### B. Token refresh

- **Recommended — lazy, at use-time.** When the engine resolves a connection for a step, if it's
  an OAuth connection whose `accessToken` is expired (or within a small skew), it uses the
  `refreshToken` at the token endpoint, persists the new tokens (encrypted), and proceeds.
  Single-flight per connection to avoid duplicate refreshes. No background job.
- Refresh failure (revoked/expired refresh token) surfaces as a **connection error** (like the
  trigger-health pattern) and the step fails with a clear "reconnect needed" message.

### C. SDK shape

- New `IOAuthConnectionType : IConnectionType` adding: `AuthorizationEndpoint`, `TokenEndpoint`,
  `IReadOnlyList<string> Scopes`, `bool UsePkce`. `Fields` still declare what the user enters
  (client id/secret; endpoints for the generic type). The flow writes `accessToken` /
  `refreshToken` / `expiresAt` into the blob — fields the type doesn't have to declare.
- A provider-agnostic `OAuthFlow` engine service builds the authorize URL (state + PKCE),
  exchanges the code, and refreshes — standard OAuth2, no per-provider code for the common case.

## Endpoints

- `POST /api/connections` — create the connection first (client id/secret/scopes/endpoints; no
  tokens yet). It shows as "not connected".
- `GET /api/connections/{id}/oauth/start` — build the authorize URL (persist `state` + PKCE
  verifier), 302 to the provider.
- `GET /api/connections/oauth/callback?code&state` — validate `state`, exchange the code, store
  tokens on the connection, redirect back to `/connections`.

## Execution semantics

- `{{connections.<name>.accessToken}}` resolves to a valid token (refreshed if needed). Other
  fields (client id, etc.) resolve as today.
- The refresh write is the **only** place a connection mutates at runtime; it's an isolated,
  single-flight update keyed by connection id.

## Phased plan

1. **Phase 1 — generic OAuth2 (BYO endpoints + client).** SDK interface, `OAuthFlow` (authorize
   URL / code exchange / refresh), the two endpoints, lazy refresh in template resolution, and a
   "Connect" button + connected/expired state in the UI. Tests-first on the pure bits (authorize
   URL build with PKCE/state, token-response parse, expiry/refresh decision).
2. **Phase 2 — a concrete provider preset** (e.g. Google or GitHub) built on Phase 1, so users
   pick it and only paste client id/secret.

## Open questions for you

1. **App model:** BYO per-connection (recommended), instance-config apps, or both?
2. **First preset:** ship Phase 1 generic-only, or also include one concrete provider — and which
   (Google / GitHub / Slack)?
3. **Refresh:** confirm lazy at use-time (recommended) vs a background refresher.
