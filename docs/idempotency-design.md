# Action idempotency keys (v3.5)

Let a side-effecting step declare an **idempotency key** so the same logical event can't fire the
side effect twice — across redeliveries, retries, and re-fires of the same trigger event.

## Where double-execution comes from

The engine re-runs a step's action in two places (`ExecuteStepHandler`):

1. **Redelivery.** Wolverine redelivers `ExecuteStep` after a crash. There's already a guard —
   `if (stepExecution is { Status: Succeeded }) return Advance(...)` — but it only protects the window
   *after* the step's completion is committed. If the process dies between the action's side effect
   landing and that commit, the redelivery re-runs the action.
2. **Retry-after-partial.** The action performs its side effect (HTTP POST lands) but then throws
   (e.g. a read timeout). `RecordFailure` → `DelayedFor` re-dispatches and the action runs again.
3. **Re-fire of the same event.** The *whole workflow* runs again for the same logical input — a
   webhook the source redelivers, an `http.poll` re-firing for the same item. (`kv.setIfAbsent` + a
   `gate` already covers "halt a duplicate run"; idempotency keys instead let the run continue but
   skip the *one* side-effecting step, returning its prior result.)

## Design — engine idempotency store (result cache)

A step opts in with a templated **idempotency key**. The engine keeps a durable record of the
**first successful result** for a key and, on any later execution with the same key, returns that
cached result *without invoking the action*.

```
IdempotencyRecord(WorkspaceId, WorkflowId, Key)  [unique]
  → ResultJson, CreatedAt
```

`ExecuteStepHandler`, for an action step with a resolved key:

1. **Lookup** `(WorkspaceId, WorkflowId, Key)`. **Hit** → `stepExecution.Complete(record.ResultJson)`,
   emit `StepCompleted`, advance — the action never runs.
2. **Miss** → invoke the action as today. On **success**, persist the record **in the same
   `SaveChanges` as `stepExecution.Complete`** (`INSERT … ON CONFLICT DO NOTHING` to absorb a race —
   on conflict we proceed; the stored result is equivalent). On **failure**, write nothing, so a
   retry re-runs (the action never succeeded).

**Scope: per workflow** (`WorkspaceId + WorkflowId + Key`) — matches `kv.*` scoping; a re-fired event
lands back in the same workflow. (Per-workspace scope considered; deferred — niche, and broader
collisions are surprising.)

**Key home: a first-class step field** `idempotencyKey` (templated, e.g.
`{{trigger.payload.orderId}}`), resolved by the engine and **never passed to the action** — mirrors
the existing first-class `Key`/`Name` step columns. (Reserved-config-field alternative — e.g.
`__idempotencyKey` stripped before the action sees it — considered; rejected as less discoverable and
"magic".) Empty/unset key = today's behavior, unchanged.

## Provider-native forwarding (defense in depth)

The store closes re-fire and post-commit-redelivery, but **not** retry-after-partial (case 2): the
action threw, nothing was stored, so the retry re-sends. The fix for that window lives at the
provider. When a key is set, `webhook.send` forwards it as an `Idempotency-Key` header so a
compliant receiver dedups. Provider-native keys and the engine store are complementary — neither
alone is sufficient.

## Guarantees (stated honestly)

- **Re-fire / post-commit redelivery of a keyed step → at most one side effect** (cached result
  returned). ✓
- **A failed action still retries** — correct; no success was recorded.
- **Retry-after-partial** (succeeded externally, then threw) is mitigated by the forwarded
  `Idempotency-Key` where the provider honors it — **not** by the store.
- This is **not** distributed two-phase commit. The crash-between-side-effect-and-commit window is
  narrowed (provider key), not eliminated. A claim-before-act record (write `Pending` before the call)
  was considered and rejected for v1: it trades double-send for a worse failure mode (lost send on a
  crash between claim and call).

## Retention

Records accumulate. `CreatedAt` supports pruning; a periodic sweep (or a TTL) prunes old keys — small
follow-up, not load-bearing for correctness (a pruned key simply allows a re-send after the window).

## Scope / sequencing (each its own commit under a v3.5 sub-tag)

1. **This design note.**
2. **Engine** — `IdempotencyRecord` + store + `ExecuteStep` integration; migration handoff (tests-first).
3. **Authoring** — `idempotencyKey` step field: `StepDefinition`/`WorkflowStep` + `AddVersion` + API +
   transfer + builder.
4. **Provider key + wrap-up** — `webhook.send` `Idempotency-Key`; CHANGELOG + recipe + release notes.

## Risks

- **Key correctness is the user's job** — too broad a key suppresses legitimate sends; too narrow
  dedups nothing. The recipe leads with `{{trigger.payload.<stableId>}}`.
- **Atomicity** — the record write must share the transaction with `stepExecution.Complete`; over-test
  the hit/miss/failed paths against real Postgres.
- **Unbounded growth** without retention — flagged above.
