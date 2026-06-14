# Trigger → lane routing

**Status:** building · grows from the branching DAG (`docs/branching-parallel-design.md`) and the
first-class trigger nodes (`docs/v3-plan.md` §7 deferral).

## Problem

Every trigger feeds the workflow's first step (order 0). A branched workflow often wants different
triggers to start at different points — e.g. a webhook that jumps straight into an "incident" lane,
while a cron runs the full probe from the top. Today the builder also hardcodes every trigger→step
edge to step 0.

## Decision

A trigger gets an optional **entry step** — the step order its fire starts the run at.

- `Trigger.EntryStepOrder : int?` — `null` means *first step by order* (today's behaviour, so
  every existing trigger is unchanged).
- `RunWorkflow` carries `EntryOrder : int?`. The handler dispatches that step instead of always
  the first; an out-of-range / missing order falls back to the first step (defensive, never throws).
- Only trigger-table fires carry an entry: **cron**, **plugin (rss/http.poll)**, **webhook**, and
  **workflow-chain**. Manual run, the `schedule` action, and retries keep the default (first step).

## Start-mid-DAG semantics

The run dispatches the entry step directly and routes forward from it exactly like any other step:

- **Linear** (no edges): runs `entry, entry+1, … last` via the inline order-next path. Earlier
  steps never run.
- **Branched** (edges): routing proceeds from the entry; only steps reachable from it run, the
  rest are never created.

**Footgun (v1, documented):** if the entry feeds a lane that later **joins** a sibling lane which
was never started, the join waits forever (its other predecessor never becomes terminal) and the
execution stays `Running`. Authors should point a trigger at a self-contained entry (a root, or a
lane head with no cross-lane join). The builder can add a reachability warning later; the engine
does not police this.

## Validation

`EntryStepOrder`, when set, must be a valid step order in the workflow's **latest** version
(`0 <= order < stepCount`). Create/Update reject out-of-range values. The engine still falls back
defensively, but the API is the gate.

## Surfaces

- **Persistence:** `Trigger.EntryStepOrder`; migration `AddTriggerEntryStep`.
- **Engine:** `RunWorkflow.EntryOrder`; `RunWorkflowHandler` resolves the entry step.
- **Dispatch:** cron / plugin-host / webhook / chaining pass `trigger.EntryStepOrder`.
- **API:** create takes `entryStepOrder?`; update takes a tri-state `entryStepOrder` (absent =
  unchanged, `null` = reset to default, number = set); `GetWorkflow` returns it per trigger.
- **Builder:** `DraftTrigger.entryStepOrder`; a "Starts at step" selector; `applyTriggers` carries
  it; `WorkflowGraph` draws each trigger→its entry step (canvas + read-only detail) instead of the
  hardcoded →step 0.

## Tests

- Engine: a trigger entry at order N runs N…end and never runs the earlier steps; `null` entry is
  unchanged (first step).
- API/validation: out-of-range entry is rejected.
