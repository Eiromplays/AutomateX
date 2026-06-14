# Deploy AutomateX on a homelab (Proxmox + Tailscale + OIDC)

A 24/7 self-host using the published GHCR images, reachable over Tailscale with automatic HTTPS,
and browser login via OIDC. The whole stack is three containers (Postgres + API + web) managed by
`docker-compose.prod.yml`, with secrets in a local `.env`.

## 1. A host for Docker on Proxmox

Either works; pick one:

- **VM (simplest, most robust):** a small Debian/Ubuntu VM (2 vCPU, 2 GB RAM, 16 GB disk is
  plenty). Docker runs with no special tweaks.
- **Unprivileged LXC (lighter):** create a Debian container, then on the Proxmox host enable the
  features Docker + Tailscale need in `/etc/pve/lxc/<id>.conf`:

  ```
  features: nesting=1,keyctl=1
  lxc.cgroup2.devices.allow: c 10:200 rwm
  lxc.mount.entry: /dev/net/tun dev/net/tun none bind,create=file
  ```

  (the last two give the container `/dev/net/tun` for Tailscale). Reboot the container.

Install Docker Engine + compose plugin:

```bash
curl -fsSL https://get.docker.com | sh
```

## 2. Get the files onto the host

You only need two files (no full clone required):

```bash
mkdir -p ~/automatex && cd ~/automatex
curl -fsSLO https://raw.githubusercontent.com/Eiromplays/AutomateX/main/docker-compose.prod.yml
curl -fsSLO https://raw.githubusercontent.com/Eiromplays/AutomateX/main/.env.example
cp .env.example .env
```

If the GHCR images are private, authenticate once with a PAT that has `read:packages`:

```bash
echo "$GHCR_PAT" | docker login ghcr.io -u <github-username> --password-stdin
```

## 3. Fill in `.env`

```bash
# Encryption key — generate once, never change (changing it orphans stored connection secrets):
openssl rand -base64 32
```

Set `POSTGRES_PASSWORD`, paste the key into `ENCRYPTION_KEY`, and set the OIDC values. Leave
`PUBLIC_BASE_URL` for the next step (it's the Tailscale hostname).

## 4. Bring it up + expose over Tailscale

```bash
docker compose -f docker-compose.prod.yml up -d
```

The web UI is now on `http://<host>:8080` inside the tailnet. To get HTTPS + a stable hostname with
no port forwarding, use **Tailscale Serve** (TLS terminated by Tailscale, valid `ts.net` cert):

```bash
tailscale serve --bg 8080
tailscale serve status      # shows https://<host>.<tailnet>.ts.net
```

Put that URL in `.env` as `PUBLIC_BASE_URL`, then `docker compose -f docker-compose.prod.yml up -d`
again so the API picks it up. (Want it reachable outside the tailnet too? `tailscale funnel --bg 8080`
publishes the same https URL to the public internet — only do that if you intend public access.)

The API already honours `X-Forwarded-Proto/Host`, so OIDC redirects and webhook URLs resolve to the
`ts.net` origin correctly behind the Tailscale → Caddy → API chain.

## 5. Register the OIDC redirect URIs

At your provider (Entra ID / Authentik / Keycloak / Auth0 / …), for this client add:

- Redirect URI: `https://<host>.<tailnet>.ts.net/signin-oidc`
- Post-logout URI: `https://<host>.<tailnet>.ts.net/signout-callback-oidc`

Then open the UI and sign in. (An optional `Auth__ApiKey` still works for scripts/API clients via
the `X-Api-Key` header even with OIDC on.)

## Updating

Versions are published to GHCR by the release workflow on `v*` tags. To move to a newer version:

```bash
docker compose -f docker-compose.prod.yml pull
docker compose -f docker-compose.prod.yml up -d
```

DB migrations apply automatically on API startup. Pinning a specific version instead of `:latest`
(e.g. `ghcr.io/eiromplays/automatex-api:v3.0.0`) makes rollbacks trivial.

**Auto-update (optional):** the release workflow fires `AUTOMATEX_DEPLOY_WEBHOOK` once new images
are in GHCR. Point that repo secret at an AutomateX webhook whose workflow SSHes back to this host
and runs `docker compose -f docker-compose.prod.yml pull && up -d` — then tagging a release deploys
itself. See [recipes/self-deploy.md](recipes/self-deploy.md).

## Troubleshooting

**OIDC redirect comes back as `http://` (login bounces).** The API builds the redirect URI from the
request scheme it sees. Behind a TLS-terminating front (Tailscale Serve → Caddy on `:80` → API),
the proxy must pass `X-Forwarded-Proto: https` *and* Caddy must trust it — the bundled `Caddyfile`
sets `trusted_proxies static private_ranges` for this. If you front the stack with your own proxy,
ensure it forwards `X-Forwarded-Proto`/`X-Forwarded-Host` and that the API trusts it.

**Login still bounces.** `PUBLIC_BASE_URL`, the OIDC redirect URI registered at the provider
(`<url>/signin-oidc`), and the hostname in the browser must be byte-for-byte identical — https, no
trailing slash.

## Plugins

Drop `<Name>/<Name>.dll` into `~/automatex/plugins/` (volume-mounted) and restart the API:

```bash
docker compose -f docker-compose.prod.yml restart api
```

## Backups

All state lives in the `automatex-postgres-data` volume. A simple nightly dump:

```bash
docker compose -f docker-compose.prod.yml exec -T postgres \
  pg_dump -U automatex automatex | gzip > automatex-$(date +%F).sql.gz
```

Back up your `.env` too — without `ENCRYPTION_KEY` the stored connection secrets can't be decrypted.
(You could even run this dump *as an AutomateX workflow* once it's up: a cron trigger → `ssh.command`.)
