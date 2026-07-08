# forEach (loops)

**Status:** shipped (v3.4 sequential) · **capped-parallel shipped v4.10** · builds on `workflow.call`
(`docs/sub-workflows-design.md`) and the durable suspend/resume keystone.

> **v4.10 update.** The capped-parallel follow-up sketched below landed, but the accumulator was
> rebuilt rather than lock-wrapped: the single `ResultsJson` blob + `NextIndex` cursor described in
> "State" are replaced by **per-item `ForEachItemResult` rows + atomic counter `UPDATE`s** (each child
> writes only its own row; a lock-free parent-status claim elects the finisher). This removed the
> O(N²) blob re-serialization and the single-row hotspot, so no row lock or optimistic-retry was
> needed. `concurrency` is a config field clamped to `[1, min(total, Engine:ForEachMaxConcurrency)]`
> (ceiling default 20). See `docs/foreach-concurrency-design.md`.

## Problem

`workflow.call` runs *one* child. Looping wants to run a sub-workflow **per item** of an array and
collect the results — "for each order, run the fulfilment flow." The hard part isn't launching; it's
**durably accumulating** N child results (which finish in any order, possibly concurrently) and
resuming the parent exactly once when all are done.

## Decision

A **`forEach`** step maps a child workflow over an array, suspends the parent (`Waiting`), and resumes
it with an **ordered results array** once every item is done. It reuses the `workflow.call` machinery
(parent link + `ResumeExecution` on child terminal); the new part is the per-item **accumulator**.

Config: `{ items, workflowId, concurrency? }`

- `items` — a template resolving to a JSON array (e.g. `{{steps.fetch.output.rows}}`). Each element
  becomes a child's `{{trigger.payload}}`.
- `workflowId` — the child workflow (same workspace).
- `concurrency` — max children in flight. v1 shipped sequential (`concurrency = 1`); capped-parallel
  shipped in v4.10 (see the update note at the top).

The step's output is `[<child result>, …]` in item order — each child result is the same
`{status, executionId, output}` shape as `workflow.call`.

## State

A per-loop **`ForEachState`** row, unique on `(ExecutionId, StepOrder)`:

- `WorkflowId`, `Total`, `Concurrency`
- `ItemsJson` — the resolved array (to launch remaining items)
- `NextIndex` — next item to launch
- `CompletedCount`
- `ResultsJson` — a fixed-length array, slot `i` filled with item `i`'s result
- `AnyFailed` — whether any child failed (for halt vs continue semantics)

Migration `AddForEachState`. Child linkage gains `ParentItemIndex` on `Execution` + `RunWorkflow`
(which slot a child's result fills) — added alongside the `AddSubWorkflowLink` columns conceptually,
in this migration.

## Flow

1. **Enter `forEach`** (engine-handled like `wait`/`workflow.call`): resolve `items`.
   - Empty array → complete the step with `[]` and advance (no suspend).
   - Else: create `ForEachState`, **suspend the parent**, launch the first `min(concurrency, Total)`
     children (each a `RunWorkflow` with `ParentExecutionId`/`ParentStepOrder`/`ParentItemIndex` +
     `Depth+1`), set `NextIndex` accordingly. Cascade those `RunWorkflow`s.
2. **A child finishes** → its terminal site cascades `ResumeExecution(parent, stepOrder, "child",
   result, ItemIndex)` (the `ItemIndex` is new on `ResumeExecution`, read from the child's
   `ParentItemIndex`).
3. **`ResumeExecutionHandler` routes by step kind:**
   - If a `ForEachState` exists for `(parent, stepOrder)` → **accumulate**: write `Results[ItemIndex]
     = result`, `CompletedCount++`, `AnyFailed |= result.failed`; if `NextIndex < Total` launch item
     `NextIndex` (and `NextIndex++`); if `CompletedCount == Total` complete the `forEach` step with the
     results array and advance the parent; otherwise the parent stays `Waiting`.
   - Else (wait / workflow.call) → today's behaviour (complete + advance).

## Concurrency correctness

The accumulator mutation in step 3 must be atomic against concurrently-finishing children.

- **v1 (sequential, `concurrency = 1`):** only one child is ever in flight, so there is no concurrent
  mutation — the accumulator updates one at a time, no locking needed. Correct and simple.
- **v1.x (capped parallel) — shipped v4.10, differently.** Rather than lock the blob's read-modify-write,
  the accumulator moved to per-item rows + atomic counter `UPDATE`s (see the v4.10 update note at the
  top), which is contention-free and needs no row lock or optimistic-retry. The launch side stayed
  trivial (a stateless `i + concurrency` chain rule); the join became lock-free instead of locked.

Idempotency: a redelivered child-resume must not double-count. Guard on the slot — only the first
write to `Results[ItemIndex]` increments `CompletedCount` (slot already filled → no-op).

## Failure semantics

Per item, a child failure lands as `{status: "Failed", …}` in its slot (data, like `workflow.call`).
The loop's own outcome respects the workflow's continue-on-failure flag: with continue-on-failure the
loop finishes and returns all results (some failed); otherwise the first failed item completes the
`forEach` step but marks it so the parent run settles `Failed` after the loop — mirroring lane
failure. (v1: collect all results; surface `AnyFailed` in the output; document the halt nuance.)

## Surfaces

- **Engine:** `forEach` recognized in `ExecuteStepHandler` (resolve items, create state, launch,
  suspend); `ResumeExecutionHandler` accumulate-vs-complete routing; `ResumeExecution.ItemIndex`;
  `Execution.ParentItemIndex`.
- **Persistence:** `ForEachState` + `AddForEachState` migration.
- **Builder:** a `forEach` editor (items template + workflow picker + concurrency); the step output is
  an array.

## Tests

- Empty array → step output `[]`, no children, run continues.
- N items → N child runs → results in item order; the parent resumes once.
- A failing item → its slot holds `status: Failed`; loop still returns all results (continue-on-
  failure) / settles Failed (halt).
- Redelivered child-resume doesn't double-count (slot-guarded).
- (When parallel lands) concurrency cap honoured; concurrent finishes don't lose results.
