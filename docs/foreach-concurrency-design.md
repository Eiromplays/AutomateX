# v4.10 — forEach capped-parallel (concurrency cap) (design)

A focused **loose-ends minor** that closes the long-standing `forEach` follow-up: run a bounded number
of loop children **in parallel** instead of strictly one at a time. Single track, engine-first
(tests-first). Rather than bolt a lock onto the existing single-blob accumulator, this replaces it with
the model that's correct *and* scales — per-item result rows plus atomic counters — so the primitive
never has to be reworked again.

## What & why

`forEach` (v3.4) maps a child workflow over an array and resumes the parent once every item is done. It
ships **sequential** (`concurrency = 1`): enter launches item 0 and suspends; each child's terminal
resume launches the next item, so exactly one child is ever in flight.

Two things block parallel today, both in the accumulator:

- **The single-blob results field.** `ForEachState.ResultsJson` is one serialized array. `Record`
  parses it, sets one slot, and re-serializes the whole thing — **O(N) per completion, O(N²) per loop**
  in CPU and DB write size — and it's a single row every parallel child would contend on. Locking it
  (pessimistic or optimistic-retry) fixes the race but keeps the quadratic write amplification and the
  hotspot. Wrong model, not just wrong lock.
- **The read-modify-write.** `Record` + `TakeNext` read the row, mutate in memory, and `SaveChanges`.
  Safe with one child in flight; a lost update / double-launch with several.

## Shape

Replace the accumulator with a relational model whose writes are contention-free and O(1):

- **Per-item result rows.** A new `ForEachItemResult` (`ExecutionId, StepOrder, ItemIndex, ResultJson,
  Failed`), unique on `(ExecutionId, StepOrder, ItemIndex)`. Each child writes **only its own row**, so
  results never contend and idempotency falls out of the unique key — a redelivered resume is an
  `INSERT … ON CONFLICT DO NOTHING` that affects 0 rows and returns early. `ForEachState` keeps only the
  loop-level counters (`Total`, `Concurrency`, `CompletedCount`, `AnyFailed`, `Completed`,
  `ChildWorkflowId`); `ResultsJson` and `NextIndex` are dropped.
- **Stateless launch rule.** The cap is maintained without any shared launch cursor: enter launches
  items `0 … min(concurrency, total) − 1`, and **item `i`'s completion launches item `i + concurrency`**
  (when `< total`). Every index `j ≥ concurrency` is launched exactly once — by the completion of
  `j − concurrency` — regardless of completion order, and the in-flight count holds at `concurrency`
  until items run out. No `NextIndex` to read-modify-write, so nothing to lock on the launch side.
- **Atomic, lock-free accumulate.** On a child terminal resume for item `i`, inside the handler's
  ambient transaction:
  1. `INSERT` its result row `ON CONFLICT DO NOTHING`; 0 rows ⇒ redelivery ⇒ return (idempotent).
  2. `ExecuteUpdate` `SET CompletedCount = CompletedCount + 1, AnyFailed = AnyFailed OR @failed WHERE Id
     = @id` — a single atomic statement; concurrent finishers serialize on the row's write lock, so no
     update is lost.
  3. Launch `i + concurrency` if `< total` (a `RunWorkflow`, cascaded).
  4. Claim completion: `ExecuteUpdate` `SET Completed = true WHERE Id = @id AND CompletedCount >= Total
     AND NOT Completed`. Exactly one child's update affects a row (the `NOT Completed` guard) — that
     child is the finisher: it reads the item rows `ORDER BY ItemIndex`, builds the ordered results
     array, completes the `forEach` step, cleans up (`ForEachState` + item rows), and advances the
     parent. Everyone else leaves the parent `Waiting`.

  No explicit `FOR UPDATE`, no optimistic-retry: Postgres row-level write locks on steps 2/4 serialize
  the counter mutations, and the whole handler commits atomically (crash ⇒ rollback ⇒ clean redelivery).
- **Config + ordering.** `ForEachConfig` gains `Concurrency` (`int`, default `1`, clamped `[1, total]`,
  optional ceiling `Engine__ForEachMaxConcurrency`, default `20`). `concurrency = 1` is byte-for-byte
  today's behaviour. Results stay item-ordered; `AnyFailed` / continue-on-failure are unchanged — only
  the number in flight changes.
- **Builder:** a small **concurrency** number field (default 1) beside the items template + workflow
  picker. Additive.

## Slice plan

**m1 — relational accumulator: per-item rows + atomic counters (engine, tests-first). No behaviour
change.**
Add `ForEachItemResult` (+ its config/`DbSet`/unique index); reduce `ForEachState` to counters
(`Concurrency`, `Completed`; drop `ResultsJson`/`NextIndex`). Rewrite enter to store `Concurrency` (still
`1`, still launches one) and the resume accumulator to the four atomic steps above with the stateless
launch rule. With `concurrency = 1` the rule reproduces the sequential path exactly. Tests: the existing
map/empty/failure cases stay green; a redelivered resume is a no-op (unique-key idempotency); the
finisher completes the step exactly once and gathers results in item order.
*Verify:* `dotnet test`; Eirik generates the `AddForEachItemResult` migration (new table; drops two
`ForEachState` columns). **Concurrency-correctness gets a subagent review, like v4.9 m2.**

**m2 — capped-parallel launch (engine, tests-first).**
Add `Concurrency` to `ForEachConfig` (clamp + ceiling); enter launches `min(concurrency, total)` children
via the existing `Cascade` fan-out. The accumulator (already lock-free) needs no change. Tests: with
concurrency `N` over `M > N` items, exactly `min(N, M)` launch initially and in-flight never exceeds `N`;
all `M` results return in item order with none lost under concurrent completion; `concurrency = 1`
reproduces the sequential path.
*Verify:* `dotnet test`.

**m3 — builder concurrency field (frontend).**
A number input (default 1, min 1) in the `forEach` editor, wired into the config. No new server surface.
*Verify:* `pnpm test/typecheck/lint/build`.

**m4 — docs + `CHANGELOG` v4.10.0 (no code).**
`CHANGELOG` v4.10.0 entry; mark the capped-parallel follow-up closed in `foreach-design.md`; drop the
"runs sequentially" line from the action description; note the accumulator's per-item-row rewrite. Final
verify: `dotnet test` + `pnpm` gate green, diff review.

## Calls made

- **Per-item rows + atomic counters over locking the blob.** Chosen for the long run: O(1) per
  completion instead of O(N²), contention-free result writes, idempotency from the unique key, and a
  natural path to per-item progress in the UI later. It costs one new table + a migration and an
  accumulator rewrite — the deliberate "do it right" cost.
- **Stateless launch (`i + concurrency`) over a shared cursor.** Removes the one remaining contended
  field (`NextIndex`) and needs no `RETURNING`, so the whole accumulator stays within EF Core's
  `ExecuteUpdate` / `ExecuteSql` surface — no raw result-set SQL.
- **Row-lock serialization over optimistic-retry.** The counter updates are single atomic statements;
  Postgres serializes concurrent finishers on the row write-lock. No retry storms on what is, by design,
  a high-contention accumulator.
- **`concurrency` default `1` = current behaviour**, opt-in, clamped `[1, total]` with a config ceiling
  so a huge array can't fan out unbounded.
- **Migration:** one `AddForEachItemResult` (new table + drop `ForEachState.ResultsJson`/`NextIndex`).
  Eirik runs it.
- **Commit scopes:** m1/m2 `feat(engine): … (v4.10 mN)`; m3 `feat(web): … (v4.10 m3)`; m4
  `docs(…): … (v4.10 m4)`.

## Deferred (own release)

**API-key rate limits / IP allowlists** (`auth-keys-design.md:150`) is the next clean loose-end, but it's
an auth-boundary feature that deserves its own focused release + subagent review rather than riding along
here.
