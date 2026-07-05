# AutomateX v4.6.0 — dynamic scoped API keys

This release replaces the single shared API key with dynamic, DB-stored, hashed keys that carry a
**scope**, a **role**, and an optional **expiry**, and are individually **revocable** and **audited**.
OIDC is unchanged and complementary.

Full notes: [CHANGELOG.md](CHANGELOG.md) · guide: [docs/recipes/api-keys.md](docs/recipes/api-keys.md).

## Highlights

- **Scoped keys.** `Instance` (admin everywhere), `Workspace` (a role in one workspace), and `Workflow`
  (a role confined to a single workflow — least privilege for MCP and one-off automations).
- **Show-once tokens.** `atx_<prefix>_<secret>`; only the SHA-256 of the secret is stored. Managed in
  **Settings → API keys** or via `POST/GET/DELETE /api/keys`.
- **Revoke + expire, immediately.** Soft revocation keeps the audit trail; the gate re-checks every
  request, so revocation/expiry are instant. Each key tracks `LastUsedAt`.
- **Per-key audit.** Actions are attributed to `key:<name>`, not a shared `api-key`.
- **Real MCP credential.** A `Workflow`-scoped key can call exactly one exposed tool and nothing else.

## ⚠ Breaking change: `Auth__ApiKey` is removed

The static shared key no longer exists in configuration. What to do:

- **On OIDC?** Nothing changes. (Scripts now use a minted key instead of a static one — see below.)
- **No identity provider?** Set **`Auth__Bootstrap=true`** and start once. AutomateX mints one `Instance`
  admin key and logs it a single time:

  ```
  AUTOMATEX BOOTSTRAP KEY: atx_…
  ```

  Copy it, sign in with the **⚿** button, mint the scoped keys you need, then **revoke the bootstrap key**.
- **Scripts / integrations / MCP clients?** They already send `X-Api-Key: <value>`. Mint a replacement key
  (a `Workflow` key for a single MCP tool; a `Workspace` key for a monitoring script; an `Instance` key for
  admin automation) and swap in the new `atx_…` value. No code change beyond the credential itself.

There is **no data migration** to run beyond the `ApiKeys` table introduced this release (applied on
startup like every other migration). Nothing is auto-generated without `Auth__Bootstrap`, so open local dev
(the compose default) keeps working with no auth.

## Upgrade checklist

1. Remove `Auth__ApiKey` from your environment/compose (it's ignored now, but drop it to avoid confusion).
2. Deploy v4.6.0. The `ApiKeys` migration applies on startup.
3. If you have no IdP: set `Auth__Bootstrap=true`, restart, grab the logged key, sign in, mint scoped keys,
   revoke the bootstrap key.
4. Reissue any script/integration/MCP credentials as minted keys and update the clients.
5. (Optional) Unset `Auth__Bootstrap` again — it's a no-op once a key exists.

## Notes

- **Least privilege by default.** Prefer the narrowest scope that works — a `Workflow`+`Viewer` key for a
  run-only MCP tool, a `Workspace`+`Viewer` key for read-only monitoring.
- **Instance keys are admin.** `Role` is ignored for `Instance` scope; a read-only-across-all-workspaces
  variant is deferred (the column already exists, so it's a no-migration addition later).
- **Per-key rate limits / IP allowlists** are out of scope for v4.6; the entity has room to add them.
