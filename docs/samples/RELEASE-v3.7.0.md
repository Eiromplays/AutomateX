# AutomateX v3.7.0

Per-tenant encryption keys and rotation — bounding the blast radius of a key compromise.

## Highlights

- **Per-tenant encryption keys.** Connection secrets are now encrypted with a per-workspace
  data-encryption key (DEK), itself wrapped by the instance key (`Encryption__Key`). A compromised key
  exposes one workspace's secrets, not the whole instance. It's transparent and backward-compatible:
  existing `v1:` ciphertext keeps decrypting; new writes use `v2:` per-tenant. Nothing to migrate.
- **Key rotation.** Rotate a workspace's DEK (re-encrypts its connections) or re-wrap every DEK under a
  new instance key — from Workspace settings → Encryption (instance-admin only), or via
  `POST /api/workspaces/{id}/rotate-key` and `POST /api/keys/rewrap`. A KEK change is bridged by
  `Encryption__PreviousKey` so old-wrapped data still reads during the swap; rotations are audited. See
  the [key-rotation recipe](docs/recipes/key-rotation.md).

## Upgrade notes

- **Migration `AddWorkspaceKeys`** — adds the `WorkspaceKeys` table (wrapped DEKs). Applies on startup
  by default (`Database__MigrateOnStartup`); no backfill — existing connections keep working as `v1:`
  until written or rotated.
- **Back up `Encryption__Key`.** It wraps every DEK; losing it loses all stored secrets.
- New optional config `Encryption__PreviousKey` — only set during a KEK rotation, then remove it.

Full history: [CHANGELOG.md](CHANGELOG.md).

---

*Remaining on the roadmap ([docs/v3.6-roadmap.md](docs/v3.6-roadmap.md)): out-of-proc plugin
sandboxing — v4.0.0, the breaking change.*
