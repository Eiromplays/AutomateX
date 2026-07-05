# Recipe: API keys

Scoped, hashed, revocable credentials for scripts, integrations, and MCP clients. They replace the old
single shared `Auth__ApiKey` (removed in v4.6) with per-key scope, role, and expiry.

## Token shape

A key looks like `atx_<prefix>_<secret>`:

- `atx_` — a fixed marker (greppable by secret scanners).
- `<prefix>` — a non-secret lookup id, stored in clear and indexed.
- `<secret>` — 32 random bytes; only its **SHA-256 hash** is stored.

The full token is shown **once**, at creation. It can't be retrieved again — if it's lost, revoke it and
mint a new one. Verification is a fast fixed-time hash compare (these are 256-bit random tokens, not
human passwords), and every request re-checks the key, so revocation and expiry take effect immediately.

## Scopes — pick the narrowest that works

| Scope | Sees / can touch | Role |
| --- | --- | --- |
| **Instance** | every workspace; instance-admin operations | always admin (`Owner`) |
| **Workspace** | one workspace only (a header naming another is refused) | the key's `Role` |
| **Workflow** | one workflow only — its runs, triggers, and state | the key's `Role` |

A `Workflow`-scoped key can't list or reach other workflows, their executions, triggers, or state, and
can't create new workflows. It's the least-privilege credential for a single automation — most useful for
MCP (see below).

`Role` is `Viewer` / `Editor` / `Owner` and means the same as a workspace membership role, just carried by
the key. It's ignored for `Instance` keys (they're always admin).

## Mint a key

**UI:** **Settings → API keys → Create a key.** Give it a name, choose a scope (and a role/workflow), and
optionally an expiry. Copy the token from the one-time dialog.

**API:** `POST /api/keys`

```jsonc
{
  "name": "grafana-readonly",
  "scope": "Workspace",         // Instance | Workspace | Workflow
  "role": "Viewer",             // ignored for Instance
  "workflowId": null,           // required for Workflow scope
  "expiresAt": "2026-12-31T23:59:59Z"  // optional; null = never
}
```

The response includes `token` — the only time you'll see it. `workspaceId` is taken from the
`X-Workspace-Id` header (the workspace you're minting in); `Instance` keys ignore it.

**Who can mint:** instance admins mint any scope. A workspace **`Owner`** mints `Workspace`/`Workflow`
keys within their own workspace, at a role no higher than their own. Editors and Viewers can't mint.

## Present a key

- **Scripts / integrations:** send it as a header — `X-Api-Key: atx_…`.
- **Browser:** click the **⚿** button and paste the key. It's exchanged for an HttpOnly, `SameSite=Strict`
  cookie via `POST /api/auth/session`, so the SPA never holds the raw key in JS-readable storage and
  SignalR authenticates from the cookie automatically.

## MCP: a key that can call exactly one tool

Expose a workflow as an MCP tool (see [mcp-server](./mcp-server.md)), then mint a **`Workflow`**-scoped key
for it. That key's `tools/list` shows only that workflow and `tools/call` refuses every other tool — hand
it to a client and it can do nothing else on your instance.

## Expiry & revocation

- **Expiry** is optional and enforced on every request; an expired key stops working with no cleanup.
- **Revoke** (UI **Revoke**, or `DELETE /api/keys/{id}`) is a soft delete — the row is kept so the audit
  trail survives, but the key stops working immediately. Same authz as minting.

## No identity provider? Bootstrap the first key

With OIDC off and no keys yet, set **`Auth__Bootstrap=true`** and start once. AutomateX mints one
`Instance` admin key and logs it a single time:

```
AUTOMATEX BOOTSTRAP KEY: atx_…
```

Copy it from the console/log, sign in with it, mint the scoped keys you actually need, then **revoke the
bootstrap key**. It's an ordinary revocable row — nothing special once you've used it. Leave the flag set
or unset thereafter; it does nothing once a key exists. Open local dev (no flag) stays open with no auth.

## Audit

Every mint and revoke is recorded (`apikey.create`, `apikey.revoke`), and because the actor is now the key
identity (`key:<name>`), every action a key takes is attributed to that specific key in the
[audit log](./audit-log.md) — not a shared `api-key` string.
