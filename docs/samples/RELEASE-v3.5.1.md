# AutomateX v3.5.1

Exactly-once-ish side effects: steps can now dedup themselves.

## Highlights

- **Action idempotency keys.** Give a side-effecting step a templated key (e.g.
  `{{trigger.payload.orderId}}`) and the engine caches its first successful result per workflow —
  returning it on any later run with the same key instead of re-invoking. Dedups re-fired events
  (a redelivered webhook, an `http.poll` re-firing for the same item) and crash redeliveries between
  a step's side effect and its commit; failures aren't cached. `webhook.send` also forwards the key
  as an `Idempotency-Key` header so a compliant receiver dedups our retries too (a header you set
  yourself wins). Authored per-step in the builder; travels with export/import. See the
  [idempotency recipe](docs/recipes/idempotency.md) and
  [design note](docs/idempotency-design.md).

It pairs with the existing `kv.setIfAbsent` + `gate` dedup: that halts a *duplicate run* up front;
an idempotency key lets the run proceed but skips a *single* side-effecting step.

## Upgrade notes

- **Migration `AddIdempotency`** — adds the `IdempotencyRecords` table and a nullable
  `WorkflowStep.IdempotencyKey` column. Applies on startup by default (`Database__MigrateOnStartup`);
  no backfill, fully opt-in (no key = unchanged behavior).

Full history: [CHANGELOG.md](CHANGELOG.md).

---

*Remaining on the v3.5 ops/hardening track: audit log + instance-admin role, per-tenant DEKs, and
out-of-proc plugin sandboxing.*
