# Recipe: the audit log

See who created, changed, ran, or deleted things — and give operators a cross-workspace view.

## Read it

Open the **Audit** page, or call `GET /api/audit`. Each entry is `{ at, actor, action, targetType,
targetId, summary }`:

```
2026-06-28T10:14Z  alice@corp.com  workflow.update     workflow 7f3…  v4
2026-06-28T10:15Z  cron            execution.succeeded execution a91… workflow 7f3…
2026-06-28T10:16Z  bob@corp.com    connection.delete   connection 22… stripe-live
```

- **Actor** is the OIDC subject/email, or `api-key` for a machine client / open instance.
- **Scope**: members see only their workspace; instance-admins see every workspace.

### Filters

`?actor=`, `?action=` (e.g. `workflow.delete`), `?targetType=`, `?since=` (ISO timestamp), `?take=`
(default 100, max 500). Admins may add `?workspaceId=` to focus one workspace.

## What's recorded

- **Config mutations** — `workflow.create/update/delete/enable/disable/import`,
  `connection.create/update/delete`, `trigger.create/update/delete/rotate-secret`,
  `workspace.create`, `member.upsert/remove`, `plugin.install`.
- **Runs** — `execution.succeeded` / `execution.failed` (actor = the run's trigger).

Summaries never contain secrets (the same masking as everywhere else).

## Instance-admins

An operator role above workspace-owner, for reading the audit log across all workspaces and managing
the instance. Grant it in config:

```
Auth__InstanceAdmins__0=admin@corp.com
Auth__InstanceAdmins__1=<oidc-subject>
```

Only consulted in OIDC mode — with no IdP (open or api-key), the caller through the gate is already an
operator. Admin status comes only from config, never from workspace data.

## Retention

Entries are append-only and accumulate. Set `Engine__AuditRetention` (a timespan, e.g. `90.00:00:00`
for 90 days) and the retention sweeper prunes anything older on each sweep. Unset (the default) keeps
the trail forever — auto-deleting audit data is opt-in.
