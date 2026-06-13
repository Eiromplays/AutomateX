# Branching Phase 2–3 — merge & parallel lanes

**Status:** proposal for buy-in · **Date:** June 2026 · follows `branching-design.md` (Phase 1 shipped).

## Phase 2 (merge) — already works

The Phase-1 reachability engine already handles a **diamond** where a conditional fork's lanes
re-converge on a shared node. Take the matched lane: the other lane is recorded `Skipped`, and the
merge node — reachable from the taken lane — runs **exactly once**. No new engine work is needed for
merges that sit below a `switch`. (Locked by `Conditional_diamond_merges_on_one_lane`.)

So "merge" is only a *new* problem in combination with **true parallel** execution, below.

## Phase 3 (parallel) — the real work

True parallel = a step with **multiple unconditional (`null`-label) outgoing edges** fans out so
**both** lanes run, and a downstream **join** node runs **once after all lanes finish**.

Where the engine stands:

- **Fan-out — partly there.** `WorkflowRouter.Route` already returns multiple `Next` for multiple
  unlabeled edges, and `NextMessage` already emits one `ExecuteStep` per next. So fan-out dispatches
  both lanes today.
- **Join — broken.** Each lane that arrives at the join node dispatches it independently → the join
  runs *once per arriving lane* instead of once.
- **Completion — broken.** The first lane to reach a terminal step calls `execution.Complete()`
  while other lanes are still running → premature completion.
- **Concurrency — unhandled.** Parallel `ExecuteStep` messages for one execution run concurrently
  (Wolverine), so join-dispatch and completion race.

## Proposed model — readiness-based dispatch

Move from "next = successors of the just-finished step" to **dispatch a successor only when it's
ready**:

- **Join rule:** when a step finishes, for each outgoing target, dispatch it **iff all of its
  incoming edges' source steps are terminal** (`Succeeded` or `Skipped`). A single-incoming
  successor is just "its one predecessor finished" — identical to today. A target reachable only via
  not-taken edges is `Skipped` (as today).
- **Atomic claim:** dispatching a step first **claims** it with an insert-if-absent on
  `StepExecution(executionId, order)` (Postgres `INSERT … ON CONFLICT DO NOTHING`). Only the claimer
  emits the `ExecuteStep` — so a join runs once even if two predecessors finish simultaneously.
- **Completion:** after a step finishes and dispatches its ready successors, the execution completes
  when **no `StepExecution` is `Pending`/`Running` and nothing new was just dispatched**.
  `Execution.Complete()` becomes idempotent (atomic `UPDATE … WHERE Status = Running`) so concurrent
  finishers can't double-complete or double-emit `ExecutionCompleted`.
- **Failure (configurable):** a workflow-level setting `OnLaneFailure` chooses between **halt**
  (default — a failed step fails the execution immediately, other in-flight lanes abandoned, exactly
  today's behaviour) and **continue** (the failed lane stops, other lanes run to completion, then the
  execution settles `Failed`). "Continue" is a natural extension of the completion accounting (a dead
  lane just stops contributing; completion still waits for in-flight lanes, then checks if any step
  failed). Default halt keeps the simple path; the toggle is surfaced in the builder in P3b.

## Pure core (tests-first)

`WorkflowRouter` gains a readiness helper — given a candidate target, the edges, and a predicate
`isTerminal(order)`, decide: **ready** (all incoming sources terminal, ≥1 `Succeeded`), **skip**
(all incoming `Skipped`), or **wait** (some predecessor still running). Locked with unit tests
(diamond, 3-way join, partial arrival) before any engine wiring.

## Builder (Phase 3b, after the engine)

Authoring parallel needs a way to give a step **multiple unconditional successors** (fan-out) — the
current `switch-routing` derivation only emits labelled switch edges + a single backbone. UI work to
add fan-out comes after the engine routes it correctly. Engine first (importable/test-drivable),
builder second.

## Open questions

1. **Concurrency model:** true concurrent lanes via atomic-claim + idempotent-completion
   (recommended — it's the actual feature), or a simpler serialized interleave (no real parallelism,
   so not worth it)?
2. **Join semantics:** the join runs once all incoming predecessors are terminal and ≥1 Succeeded
   (`all`-join). Confirmed.
3. **Lane failure:** configurable `OnLaneFailure` = halt (default) | continue. Confirmed.
4. **Phasing:** **P3a** = pure readiness core (tests-first) → engine join + completion + atomic
   claim (halt) → **P3b** = continue-on-failure path + builder fan-out authoring + the toggle.
