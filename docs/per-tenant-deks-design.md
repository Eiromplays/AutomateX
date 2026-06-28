# Per-tenant DEKs + secret rotation (v3.7)

Give each workspace its own data-encryption key so a single compromised key exposes one tenant's
secrets, not the whole instance — and add a rotation path. Backward-compatible: existing ciphertext
keeps decrypting.

## Today

`SecretCipher` (`Engine/Security`) is the one chokepoint: AES-256-GCM under a single instance key
(`Encryption__Key`, env-only, never stored), wire format `v1:` + base64(`nonce|tag|ciphertext`). Only
`v1:` exists — the versioned prefix is the seam this builds on. `Connection.EncryptedSecrets` is the
only consumer today (one encrypted JSON blob per connection).

## Envelope scheme

Two key tiers:

- **KEK** (key-encrypting key) = the existing `Encryption__Key`. Stays env-only.
- **DEK** (data-encryption key) = one per workspace, random 32 bytes, **stored wrapped** (KEK-encrypted)
  in the DB. Generated lazily on a workspace's first encrypt.

New wire format **`v2:`** + base64(`dekVersion | nonce | tag | ciphertext`) — the secret is encrypted
with the workspace DEK; `dekVersion` selects which (for rotation). Decryption loads that workspace's
DEK, version `dekVersion`, and unwraps it with the KEK.

```
WorkspaceKey(WorkspaceId, Version, WrappedDek, Active, CreatedAt)   PK (WorkspaceId, Version)
```

## Crypto refactor

Split the chokepoint so crypto stays pure and the tenant logic is testable:

- **`SecretCipher`** becomes a key-agnostic primitive: `Encrypt(plaintext, key)` / `Decrypt(ct, key)`
  (raw AES-GCM, no format prefix decisions).
- **`TenantCipher`** (new, caller-facing) takes a **workspaceId** and owns the format:
  - `Encrypt(plaintext, workspaceId)` → loads/creates the workspace's active DEK (unwrap with KEK,
    cached), encrypts, writes `v2:`.
  - `Decrypt(ciphertext, workspaceId)` → `v1:` decrypts with the KEK directly (back-compat, ignores
    workspace); `v2:` loads the workspace DEK of the embedded version and decrypts.
- A small **`DataKeyService`** resolves + caches unwrapped DEKs (backed by `WorkspaceKey`), the only
  place a DEK exists unwrapped (in memory).

**Callers gain workspace context** (they already have it): `CreateConnection`/`UpdateConnection` (the
`ws`), and decryption in `ConnectionResolver` (`execution.WorkspaceId`).

## Rotation

- **KEK rotation** — re-wrap every `WorkspaceKey.WrappedDek` with the new KEK. No secret is
  re-encrypted. Support old+new KEK during the pass (decrypt-wrap tries new, falls back to old).
- **DEK rotation** (per workspace) — add a new DEK version, re-encrypt that workspace's connections to
  `v2:` with it, mark the old version inactive (kept until nothing references it).
- Exposed as **instance-admin-gated** operations (reuses v3.6's `IsInstanceAdmin`), runnable without
  downtime — old prefixes/versions stay readable through the pass.

## Backward compatibility & rollout

- `v1:` ciphertext **always** decrypts via the KEK — no forced migration.
- New writes use `v2:` per-tenant by default once this ships.
- An optional **backfill** upgrades existing `v1:` connections to per-tenant `v2:` (re-encrypt pass);
  not required.

## Tests-first

- Round-trip a secret under a workspace DEK (`v2:`).
- **Cross-tenant isolation:** workspace A's DEK cannot decrypt workspace B's `v2:` ciphertext.
- A `v1:` ciphertext still decrypts (back-compat).
- KEK rotation re-wraps DEKs; pre-rotation secrets still read.
- DEK rotation re-encrypts a workspace; the retired version still reads until the pass completes.

## Scope / sequencing (each its own commit under v3.7)

1. **This design note.**
2. **Crypto core** — `SecretCipher` primitive + `DataKeyService` + `TenantCipher` + `v2:` format +
   `WorkspaceKey` table + migration (tests-first).
3. **Wire callers** — connections create/update + `ConnectionResolver` decrypt pass `workspaceId`.
4. **Rotation** — KEK re-wrap + per-workspace DEK rotate, instance-admin-gated (+ tests).
5. **Wrap-up** — CHANGELOG + recipe + release notes; deploy-doc note on **backing up the KEK**.

## Risks

- **Key-management bugs lose data.** Keep `TenantCipher`/`SecretCipher` the only path; a DEK is never
  persisted unwrapped; the KEK must be backed up (losing it loses every DEK and all secrets).
- **Cache coherence** — an unwrapped-DEK cache must invalidate on rotation.
- **Marginal for single-tenant** — per-tenant DEKs mainly bound blast radius in multi-tenant; for a
  solo homelab it's defense-in-depth. Back-compat means it costs such instances nothing.

---

**Decisions to confirm:** (1) envelope = per-workspace DEK wrapped by the instance KEK, new `v2:`
format; (2) `SecretCipher` split into a key-agnostic primitive + `TenantCipher`/`DataKeyService`;
(3) rotation gated behind instance-admin; (4) `v2:` default for new writes, `v1:` stays readable, backfill optional.
