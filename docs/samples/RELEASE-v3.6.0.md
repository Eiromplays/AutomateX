# AutomateX v3.6.0

The exposed/multi-tenant tier begins: a durable audit trail and an operator role.

## Highlights

- **Audit log.** An append-only record of who did what. Every config mutation — workflows,
  connections, triggers, workspaces, members, plugins (create/update/delete) — is captured with
  actor, action, target, and a short summary, and every execution settle logs
  `execution.succeeded`/`execution.failed`. Read it on the new **Audit** page or `GET /api/audit`,
  with actor/action/target/time filters. Members see their workspace; instance-admins see all. See the
  [audit-log recipe](docs/recipes/audit-log.md).
- **Instance-admin role.** A role above workspace-owner for operators, granted by config
  (`Auth__InstanceAdmins` = OIDC subjects/emails; open and api-key callers are operators by default).
  Admins read the audit log across every workspace.

## Upgrade notes

- **Migration `AddAuditLog`** — adds the `AuditEntries` table. Applies on startup by default
  (`Database__MigrateOnStartup`); no backfill.
- The audit endpoint sits under the normal `/api` auth gate; capture is automatic (no per-workflow
  setup).

Full history: [CHANGELOG.md](CHANGELOG.md).

---

*Remaining on the v3.6 roadmap ([docs/v3.6-roadmap.md](docs/v3.6-roadmap.md)): per-tenant DEKs +
secret rotation, and out-of-proc plugin sandboxing.*
