# Recipe: automatic database backups

AutomateX backing up its own database, on a schedule, with rotation — a cron workflow that SSHes to
the deploy host and runs `pg_dump`. Import the **Nightly database backup** template (Templates →
Use template) for a ready-made version, or build it from the steps below.

```
cron (03:00)
   └─ ssh.command → docker compose exec postgres pg_dump | gzip > backups/automatex-<date>.sql.gz
                    && delete dumps older than 14 days
```

## 1. A connection to the host

Generate a dedicated key on the deploy host and add the public half to `~/.ssh/authorized_keys`:

```bash
ssh-keygen -t ed25519 -f automatex_backup -N ""
```

Create an `ssh` connection in AutomateX with:

| Field | Value |
| --- | --- |
| `host` | the host's Tailscale IP or LAN IP |
| `username` | the SSH user that owns `~/automatex` |
| `privateKey` | the contents of `automatex_backup` (private key) |

(Optionally pin the host key with `hostFingerprint` = `SHA256:…` from
`ssh-keygen -lf /etc/ssh/ssh_host_ed25519_key.pub`.)

## 2. The workflow

One `ssh.command` step on a `0 3 * * *` cron trigger. The command makes the backup dir, dumps the DB
through the running Postgres container, gzips it with a dated name, then prunes dumps older than 14
days:

```sh
mkdir -p ~/automatex/backups &&
docker compose -f ~/automatex/docker-compose.prod.yml exec -T postgres \
  pg_dump -U automatex automatex | gzip > ~/automatex/backups/automatex-$(date +%F).sql.gz &&
find ~/automatex/backups -name 'automatex-*.sql.gz' -mtime +14 -delete
```

A non-zero exit fails the step, so a broken backup shows as a **failed execution**.

## 3. Get told when a backup fails

`ssh.command` failing fails the execution — chain a notifier to it: a second workflow with a
**workflow trigger** ("when *Nightly database backup* fails") whose one step is `pushover.send` /
`matrix.send`. Now a failed dump pages you instead of dying silently in the history.

## 4. Restoring

```bash
gunzip -c ~/automatex/backups/automatex-2026-06-14.sql.gz | \
  docker compose -f ~/automatex/docker-compose.prod.yml exec -T postgres psql -U automatex automatex
```

Back up `ENCRYPTION_KEY` (in `.env`) separately — without it the dumped connection secrets can't be
decrypted on restore.

> Off-box copies: add a second `ssh.command` (or an `http.request` to object storage) after the dump
> to ship the file somewhere that isn't the machine you're backing up.
