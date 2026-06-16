# Security Policy

AutomateX is self-hosted automation software that holds credentials and can execute actions against
your own infrastructure (SSH, HTTP, messaging). We take its security posture seriously.

## Reporting a vulnerability

Please report vulnerabilities **privately**, not in public issues or discussions.

- Use GitHub's **[Report a vulnerability](https://github.com/Eiromplays/AutomateX/security/advisories/new)**
  (Security → Advisories) to open a private advisory.
- Include affected version/commit, a description, reproduction steps, and impact.

You'll get an acknowledgement, and we'll work with you on a fix and coordinated disclosure. Please
give a reasonable window to address the issue before any public disclosure.

## Supported versions

AutomateX is pre-1.0-style versioned and ships frequently. Security fixes target the **latest
release**; please upgrade to the newest version before reporting, and pin to a release tag rather
than running unpinned `latest` in production.

## Security model (what to know when self-hosting)

- **Connection secrets** are encrypted at rest (AES-256-GCM). The master key comes from
  `Encryption__Key` and is **never stored in the database** — lose it and stored secrets are
  unrecoverable. Secrets are write-only through the API (never returned), and **masked** (`***`) in
  step outputs, errors, and live events. Masking is best-effort: a secret an action transforms
  (re-encodes, splits) can't be recognized, so don't deliberately echo secrets.
- **Authentication** is a tri-state: open (local default, trusted network only), API key
  (`Auth__ApiKey`), or OIDC. **Do not expose an open-mode instance to the internet.** With OIDC,
  the browser holds only an HttpOnly cookie; refresh-token sessions track the IdP so a revoked user
  is signed out at the next refresh boundary.
- **Workspaces** isolate workflows, connections, and execution data; connection resolution is
  workspace-scoped in the engine.
- **Plugins run with full host trust.** An uploaded or dropped-in plugin is arbitrary code in the
  API process. Upload is gated behind `Engine__AllowPluginUpload` (default **off**); catalog
  installs are sha256-verified before touching disk. Only install plugins you trust.
- **Webhook triggers** use per-trigger secrets (capability URLs), shown once and validated in
  fixed time; `/api/webhooks` sits outside the global API-key gate so third parties never hold the
  instance key. Rotate with `POST /api/triggers/{id}/rotate-secret`.
- **SSH** supports host-key pinning (`hostFingerprint`); pair deploy keys with a forced command so a
  leaked key can only run the blessed script. See [docs/recipes/self-deploy.md](docs/recipes/self-deploy.md).

Anything that can redeploy or act on your infrastructure is part of your trusted computing base —
treat the instance, its `Encryption__Key`, and its database accordingly.
