# Recipe: idempotency keys

Stop a side-effecting step from firing twice for the same logical event — across re-fires, crash
redeliveries, and retries.

## Use it

Give the step an **idempotency key** (a templated string) in the builder's per-step "Idempotency
key" field, or via the API (`steps[].idempotencyKey`). Pick something stable that identifies the
event:

```
http.request   "fetch order"   GET https://api.example.com/orders/{{trigger.payload.id}}
webhook.send   "charge"        idempotencyKey = {{trigger.payload.id}}
                               url = https://payments.example.com/charge
                               body = { "order": "{{trigger.payload.id}}", "amount": 42 }
```

The first time the `charge` step succeeds for a given `id`, its result is cached. Any later run of
that workflow with the same `id` returns the cached result **without calling the action again** — so
the same order is never charged twice, no matter how the run is re-triggered.

## What it covers

- **Re-fires of the same event** — a webhook the source redelivers, an `http.poll` re-firing for the
  same item. The keyed step is skipped; the run continues with the cached result.
- **Crash redeliveries** — a process death between a step's side effect and its commit. On retry the
  keyed step returns the cached result instead of repeating the call.
- **Provider-level retries** (`webhook.send` only) — the key is also sent as an `Idempotency-Key`
  header, so a compliant receiver dedups the narrow "succeeded, then the connection dropped" window
  that the cache can't see. (A header you set yourself wins.)

## What it doesn't

- A **failed** action is never cached — retries re-run it, which is what you want.
- It's per **workflow** (`workflow + key`), not global. The same key in two different workflows is two
  different records.
- It's not distributed two-phase commit. The cache plus the provider header narrow the
  side-effect-then-crash window; for hard exactly-once you still want a provider that honors
  idempotency keys.

## Key design

- **Too broad** a key (e.g. a constant) suppresses legitimate repeat sends. **Too narrow** (e.g.
  `{{execution.id}}`, unique per run) dedups nothing. Use the id of the *thing* you're acting on.
- An unresolvable key (`{{trigger.payload.id}}` when there's no `id`) fails the step deterministically
  — better a loud failure than a silent un-deduped send.

## vs. `kv.setIfAbsent` + gate

[Dedup & state](./dedup-and-state.md) halts a *duplicate run* up front (run-once-per-key). Idempotency
keys instead let the run proceed but skip a *single* side-effecting step, returning its prior result —
use them when the rest of the workflow should still run.
