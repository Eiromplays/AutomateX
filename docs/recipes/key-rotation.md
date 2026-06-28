# Recipe: encryption keys & rotation

How AutomateX encrypts connection secrets, and how to rotate keys when you need to.

## Two key tiers

- **KEK** — the instance key, `Encryption__Key` (32 base64 bytes, env/config only, never stored).
- **DEK** — a per-workspace data-encryption key, stored **wrapped** (KEK-encrypted) in the database.
  Each workspace's connection secrets are encrypted with its own DEK.

A compromised DEK exposes one workspace; the KEK is never on disk. Both `v1:` (legacy, single-KEK) and
`v2:` (per-tenant) ciphertext decrypt transparently — you don't migrate anything to adopt this.

> **Back up `Encryption__Key`.** It wraps every DEK. Lose it and every stored secret is unrecoverable.

## Rotate a workspace's key

When a workspace's secrets may be exposed, mint a fresh DEK and re-encrypt that workspace's
connections — no downtime, no restart:

- **UI:** Workspace settings → Encryption → **Rotate workspace key** (instance-admins only).
- **API:** `POST /api/workspaces/{id}/rotate-key` → `{ version, reEncrypted }`.

The old DEK version is retired but kept, so anything still referencing it stays readable.

## Rotate the instance key (KEK)

Changing the KEK re-wraps every DEK; the DEKs (and connection ciphertext) are unchanged. Do it with a
transition key so there's no downtime:

1. Set `Encryption__Key` to the **new** key and `Encryption__PreviousKey` to the **old** one. Restart.
   Decryption now tries the new key, falling back to the old — everything keeps working.
2. Re-wrap: Workspace settings → Encryption → **Re-wrap all keys**, or `POST /api/keys/rewrap`. Every
   DEK is now wrapped under the new key.
3. Remove `Encryption__PreviousKey` and restart. The old key is no longer needed.

## Who can rotate

Both actions are **instance-admin** only (`Auth__InstanceAdmins`, or any caller in open/api-key mode —
see [audit-log](./audit-log.md)). Every rotation is recorded in the audit log (`key.rotate-workspace`,
`key.rewrap`).
