# AutomateX v3.8.0

Ops polish: bound the growth of the long-lived tables.

## Highlights

- **Retention pruning.** A background sweeper now deletes audit entries and idempotency records past
  configurable windows — `Engine__AuditRetention` and `Engine__IdempotencyRetention` (timespans, e.g.
  `90.00:00:00`). Both are **opt-in**: unset keeps everything forever, joining the existing
  `Engine__ExecutionRetention`. See the [audit-log](docs/recipes/audit-log.md) and
  [idempotency](docs/recipes/idempotency.md) recipes.

## Upgrade notes

- **No migration.** Config-only. Existing instances are unchanged until you set a window.
- Audit retention defaults to keep-forever on purpose (it's often compliance data); set it explicitly
  if your policy says otherwise.

Full history: [CHANGELOG.md](CHANGELOG.md).

---

*Next on the roadmap ([docs/v3.6-roadmap.md](docs/v3.6-roadmap.md)): out-of-proc plugin sandboxing —
v4.0.0, the breaking change, starting with a design spike.*
