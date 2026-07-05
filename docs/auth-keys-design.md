# Design: dynamic scoped API keys (v4.6)

Replaces the single shared `Auth__ApiKey` with dynamic, DB-stored, hashed API keys that carry a
**scope** (instance / workspace / workflow), a **role**, optional **expiry**, and are individually
**revocable** and **audited**. OIDC is unchanged and complementary. The static config key is **removed
outright in v4.6** (it wasn't in real use); an opt-in first-run **bootstrap key** takes over the "no IdP"
story — no static secret ever lives in config again.

## Why

The shared key is coarse and dangerous: anyone holding it is `Owner` in every workspace **and**
`IsInstanceAdmin`, forever, with no per-key scope, no revocation without editing config + restarting, no
expiry, and audit that attributes everything to the literal string `"api-key"`. Scoped keys make least
privilege possible (a run-only key for one workflow, a read-only monitoring key for one workspace) and
give MCP a real per-workflow credential.

## Token format

```
atx_<prefix>_<secret>
```

- `atx_` — fixed brand marker (recognisable, greppable by secret scanners).
- `<prefix>` — 12 chars base62, random, **unique + indexed**, stored in clear. The lookup id.
- `<secret>` — 32 random bytes, base64url. Only its **SHA-256 hash** is stored.

Verification: split on `_`, look up the row by `prefix` (O(1), indexed), then
`CryptographicOperations.FixedTimeEquals(SHA256(secret), row.SecretHash)`; reject if revoked or expired;
stamp `LastUsedAt` (throttled to ~once/min to avoid a write per request). The full token is shown
**once** at creation and never retrievable.

SHA-256, not bcrypt/argon2: these tokens are 256-bit random, not human passwords — a fast hash is the
correct, standard choice and avoids a KDF on every request. (Webhook secrets already follow this.)

## Entity — `ApiKey`

```
Id            Guid (v7)
Name          string           // human label, e.g. "grafana-readonly"
Prefix        string           // unique, indexed — non-secret lookup id
SecretHash    byte[]           // SHA-256 of the secret half
Scope         ApiKeyScope      // Instance | Workspace | Workflow
WorkspaceId   Guid?            // required for Workspace/Workflow scope
WorkflowId    Guid?            // required for Workflow scope
Role          WorkspaceRole    // Viewer/Editor/Owner — meaning within the scope
CreatedBy     string           // actor who minted it
CreatedAt     DateTimeOffset
ExpiresAt     DateTimeOffset?  // null = never
LastUsedAt    DateTimeOffset?
RevokedAt     DateTimeOffset?  // soft-delete; kept for audit
```

- `Scope=Instance` is the admin tier — implicit `Owner` in every workspace + `IsInstanceAdmin`. `Role`
  is ignored for instance keys (or pinned to `Owner`).
- Unique index on `Prefix`; index on `(WorkspaceId)` for listing.
- Soft-revoke (keep the row) so audit trails survive; a sweeper can hard-delete long-revoked/expired rows
  later if desired.

`ApiKeyScope` is a new enum; `Role` reuses the existing `WorkspaceRole` ladder.

## Request resolution

A small `ApiKeyContext` (record) is attached to `HttpContext.Items` when a request authenticates via a
key: `{ Scope, WorkspaceId?, WorkflowId?, Role, Actor }`. Populated by an `ApiKeyResolver` (scoped
service, DB access) invoked from the gate.

### `AuthGateMiddleware` (rewired)

Order for a protected path:

1. `X-Api-Key` header (or `automatex-key` cookie) starting `atx_` → `ApiKeyResolver.ResolveAsync` →
   on hit, set `ApiKeyContext`, continue; on miss/expired/revoked → 401.
2. OIDC cookie principal authenticated → continue (no `ApiKeyContext`; role via membership as today).
3. Otherwise 401.

Open mode (no `ApiKey` rows, no OIDC) stays fully open for local dev — the gate engages once the instance
has any key or OIDC configured. The resolver's DB lookup is cheap and only runs for `atx_`-prefixed
values. `Auth__ApiKey` no longer exists.

### `WorkspaceAccess` (scope-aware)

`GetRoleAsync` / `AuthorizeAsync` consult `ApiKeyContext` first:

- **Instance** → `Owner` for any workspace; `IsInstanceAdmin` true; actor `key:<name>`.
- **Workspace** → the key's `Role`, but **only** for its `WorkspaceId`; a request targeting another
  workspace (via `X-Workspace-Id`) → `null` (403). For scoped keys the effective workspace is the key's
  own — the header can't widen it.
- **Workflow** → same as workspace plus a workflow gate (below).
- No `ApiKeyContext` → existing OIDC/open logic untouched.

`GetActor` gains a `key:<name>` form so audit attributes actions to the specific key, not `"api-key"`.

### Workflow-scope enforcement

Most endpoints are workspace-scoped; a workflow-scoped key must not reach beyond its one workflow. New
helper `WorkspaceAccess.AuthorizeWorkflowAsync(context, workflowId, required)`: passes only if the
workspace role check passes **and** (when the key is `Workflow`-scoped) `workflowId == key.WorkflowId`.
Applied to the workflow-addressed endpoints (get/run/update/delete workflow `{id}`, its executions,
triggers, state, MCP call). Cross-workflow list endpoints filter to the permitted workflow (or 403). The
headline use case is MCP: hand a client a `Workflow`+`Runner` key that can call exactly one tool.

## Management API (VSA slices)

- `POST /api/keys` — mint; returns the full token **once**. Instance admin mints any scope; a workspace
  `Owner` mints keys within their workspace at a role `<=` their own. Body: name, scope, workspaceId?,
  workflowId?, role, expiresAt?.
- `GET /api/keys` — list (never secrets): name, prefix, scope, target, role, lastUsedAt, expiresAt,
  revokedAt. Instance admin sees all; workspace owner sees their workspace's.
- `DELETE /api/keys/{id}` — revoke (soft). Same authz as create.

## Bootstrap (retiring the shared key)

`Auth__ApiKey` is deleted from `AuthOptions`. The no-IdP bootstrap is an opt-in trigger, not a stored
secret:

- **`Auth__Bootstrap: true`** (a boolean, grants nothing itself): on startup, if OIDC isn't configured
  and there are zero non-revoked `ApiKey` rows, generate one **Instance** admin key and print it to the
  console/log **once** (`AUTOMATEX BOOTSTRAP KEY: atx_…`) — the Vault/Grafana pattern. The operator
  copies it, signs in, mints more; it's a normal revocable row thereafter. Left unset, an OIDC-only or
  open-dev instance is unaffected.
- No auto-generation without the flag, so open local dev (compose default) keeps working with no auth.
- Browser login (`POST /api/auth/session`) accepts any valid key and stores it in the HttpOnly cookie,
  resolved by the same path; the SPA still never sees a raw key in JS.

## Audit / rate-limit

Actor is now the key identity, so existing audit + rate-limit keying gets per-key granularity for free.

## Slice plan (buildable commits)

1. `ApiKey` entity + `ApiKeyScope` + config + **migration**; token mint/hash/verify service + tests
   (engine-only, no wiring).
2. `ApiKeyResolver` + gate integration for **Instance + Workspace** scope; `WorkspaceAccess` scope-aware;
   actor plumbing + tests.
3. **Workflow-scope** enforcement (`AuthorizeWorkflowAsync`) + MCP integration + tests.
4. Management API (`POST/GET/DELETE /api/keys`) with mint-scope authz + tests.
5. Remove `Auth__ApiKey`; add `Auth__Bootstrap` opt-in first-run instance key; `/api/auth/session`
   accepts DB keys.
6. Frontend: keys page (create → show-once modal, list, revoke); deprecation banner; nav.
7. Wrap-up: CHANGELOG, `docs/recipes/api-keys.md`, `RELEASE-v4.6.0.md`.

Each slice stays green (`dotnet test`; web `pnpm test/typecheck/lint/build`); migration is yours to run.

## Calls made

- **`Auth__ApiKey` removed in v4.6** (not graceful) — it wasn't in use; the `Auth__Bootstrap` opt-in
  replaces the no-IdP path with no static secret.
- **Instance keys are always admin** (`Role` ignored for `Scope=Instance`). A read-only-across-all-
  workspaces variant is deferred; the `Role` column already exists, so it's a no-migration add later.
- **Per-key rate limits / IP allowlists:** out of scope for v4.6; the entity has room to add later.
```
