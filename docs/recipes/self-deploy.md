# Recipe: self-deploying AutomateX

The platform updates itself and announces it on Matrix — the v1 party trick, rebuilt on v2's
encrypted connections, capability-URL webhooks and per-step durability.

```
git tag v2.x → GitHub Actions builds + pushes GHCR images
                  └─ curl → AutomateX webhook trigger (capability URL + secret)
                              └─ step 0  ssh.command  → detached `docker compose pull && up -d` on the host
                              └─ step 1  matrix.send  → "🚀 AutomateX v2.x deploying…"
                                            (execution completes — *then* the containers restart)
```

## 1. Publish the plugins

Into the compose-mounted `./plugins` folder (or your `Engine__PluginsPath`):

```bash
dotnet publish src/Plugins/AutomateX.Plugins.Ssh    -c Release -o plugins/AutomateX.Plugins.Ssh
dotnet publish src/Plugins/AutomateX.Plugins.Matrix -c Release -o plugins/AutomateX.Plugins.Matrix
```

Restart the api container and confirm `ssh.command` + `matrix.send` appear in `GET /api/actions`.

## 2. Prepare the host: a key that can only deploy

Generate a dedicated keypair (`ssh-keygen -t ed25519 -f automatex_deploy -N ""`) and bind it to a
**forced command** in `~/.ssh/authorized_keys` on the deploy host — whatever command the workflow
sends, sshd runs only the blessed script:

```
command="/opt/automatex/update.sh",no-port-forwarding,no-agent-forwarding,no-X11-forwarding,no-pty ssh-ed25519 AAAA… automatex-deploy
```

`/opt/automatex/update.sh` (mode 0755) — **the detach is the whole trick**: the SSH session returns
immediately, the step records success, the execution finishes, and only then does the restart kill
the very process that ordered it. Without the detach, the deploy execution dies mid-flight and the
StuckExecutionSweeper later flags your release as a casualty.

```sh
#!/bin/sh
nohup sh -c 'sleep 5 && cd /opt/automatex && docker compose pull && docker compose up -d' \
  >> /var/log/automatex-update.log 2>&1 &
echo "update scheduled"
```

For the [homelab setup](../deploy-homelab.md) (GHCR images via `docker-compose.prod.yml` in
`~/automatex`), the inner command is:

```sh
cd ~/automatex && docker compose -f docker-compose.prod.yml pull && docker compose -f docker-compose.prod.yml up -d
```

The release pipeline already fires `AUTOMATEX_DEPLOY_WEBHOOK` after the images publish (with a
`{"version": "..."}` body), so once that secret holds your webhook's capability URL, tagging a
release deploys itself — and the version badge flips to the new version once it restarts.

Grab the host key fingerprint for pinning: `ssh-keygen -lf /etc/ssh/ssh_host_ed25519_key.pub`
(the `SHA256:…` token).

## 3. Connections (encrypted, write-only, masked)

| Connection | Fields |
| --- | --- |
| `deploy` | `privateKey` = contents of `automatex_deploy` (the private key file) |
| `matrix` | `accessToken` = a Matrix account/bot token (Element: Settings → Help & About → Access Token, or provision a dedicated bot user) |

## 4. The workflow

`POST /api/workflows`:

```json
{
  "name": "self-deploy",
  "description": "Pull new images, restart, announce on Matrix.",
  "steps": [
    {
      "actionType": "ssh.command",
      "name": "Update containers",
      "config": {
        "host": "your-server.example.com",
        "username": "deploy",
        "command": "deploy {{trigger.payload.version}}",
        "privateKey": "{{connections.deploy.privateKey}}",
        "hostFingerprint": "SHA256:<from step 2>"
      }
    },
    {
      "actionType": "matrix.send",
      "name": "Announce",
      "config": {
        "homeserverUrl": "https://matrix.org",
        "accessToken": "{{connections.matrix.accessToken}}",
        "roomId": "!yourroom:matrix.org",
        "message": "🚀 AutomateX {{trigger.payload.version}} deploying — triggered by {{trigger.payload.actor}} ({{steps.0.output.stdout}})"
      }
    }
  ]
}
```

Notes:

- The forced command ignores the `command` we send (it's available to `update.sh` as
  `$SSH_ORIGINAL_COMMAND` if you ever want the version there) — the key *cannot* run anything else.
- `matrix.send` transaction ids are deterministic per execution step, so even if the step is
  retried you get exactly one room message.
- Add a webhook trigger to the workflow and save the one-time capability URL
  (`/api/webhooks/{id}?secret=…`).

## 5. Fire it from the release pipeline

The right moment is *after images exist in GHCR*, so trigger from the end of `release.yml` rather
than a GitHub `release` webhook (which fires before the build):

```yaml
  - name: Trigger self-deploy
    run: |
      curl -fsS -X POST "${{ secrets.AUTOMATEX_DEPLOY_WEBHOOK }}" \
        -H 'Content-Type: application/json' \
        -d "{\"version\":\"${GITHUB_REF_NAME}\",\"actor\":\"${GITHUB_ACTOR}\"}"
```

with repo secret `AUTOMATEX_DEPLOY_WEBHOOK` = the full capability URL from step 4.

Alternative: a GitHub repo webhook (Settings → Webhooks → the capability URL, `workflow_run`
events) — richer payloads (`{{trigger.payload.workflow_run.conclusion}}`), no workflow edit, but
you'll want to ignore non-`completed` deliveries.

## Pull variant — no public ingress (best for Tailscale-only)

The push webhook above needs to be reachable from GitHub's cloud, so behind Tailscale it requires
`tailscale funnel` (public). If you'd rather expose nothing, flip it around: let AutomateX **poll**
GitHub for new releases and deploy itself — outbound only, fully private.

Import the **Self-deploy on new release** template (Templates → Use template). It comes with an
`http.poll` trigger on `https://api.github.com/repos/<owner>/<repo>/releases` and the same detached
`ssh.command` + a notify step — just add the `ssh` connection and edit host/username/paths. Needs the
Feed plugin (for `http.poll`). The poll fires when a new release appears (content-hash dedup), so it
catches pre-releases too, unlike the "latest" endpoint.

### Dedup on the release tag

`http.poll` hashes the *whole* releases response, so an incidental feed change (an asset's download
count, another pre-release) can re-fire for a tag you already deployed. The template guards against
this with two leading steps:

```
kv.setIfAbsent   key = "deployed:{{trigger.payload.json.0.tag_name}}"
gate             value = {{steps.0.output.acquired}}, isTruthy = true
ssh.command      pull + restart
```

`kv.setIfAbsent` claims the tag (`acquired = true` only the first time); the gate halts every later
fire for the same tag, so a release deploys exactly once. If you built the workflow before this was
added, drop those two steps in ahead of the SSH step. See
[dedup & durable state](./dedup-and-state.md). (`kv.*` are built-in — no plugin needed.)

## Security posture

The deploy credential is encrypted at rest (AES-256-GCM), write-only through the API, masked
(`***`) in every step output and event, scoped to one workspace — and even if exfiltrated, the
forced command means it can only ever say "run the update script". Pin `hostFingerprint` so a
DNS/MitM detour can't harvest it. Rotate the webhook secret (`POST /api/triggers/{id}/rotate-secret`)
like any credential. What remains is the irreducible truth of CD: a thing that can redeploy you is
part of your trusted computing base.
