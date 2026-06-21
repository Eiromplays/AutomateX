# Try/catch error branches

**Status:** building (v3.2) · grows from the branching DAG (`docs/branching-design.md`) and the
switch router (`src/AutomateX/Engine/WorkflowRouter.cs`).

## Problem

A failed step today has two outcomes, both terminal for the lane: **halt** (the first failure fails
the whole execution) or, with `continueOnFailure`, the lane dies and the execution settles `Failed`
once everything stops. Neither lets you *handle* a failure — retry an alternative, notify, write a
fallback, then carry on. The `gate` is the linear-halt primitive and `switch` is the conditional
fork; what's missing is the **failure fork**.

## Decision

A step can have an **error edge** — an outgoing edge with the reserved label `"error"`. When the step
fails *after its retries are exhausted*, the router follows the error edge instead of halting, and the
failure is exposed to the error lane as `{{steps.<key>.error}}`. A caught failure is **not** an
execution failure.

This mirrors how `switch` already works: `null` label = unconditional, `"default"` = switch fallback,
and now `"error"` = failure path. **No schema change** — error edges are ordinary `WorkflowEdge` rows
with `Label = "error"`, exactly as `"default"` is today.

- `WorkflowRouter` gains an error-routing mode: on failure, take only `"error"` edges (no `"default"`
  fallback); the step's normal (success) successors are skipped.
- Precedence: an error edge is explicit handling, so it wins over both halt and `continueOnFailure`.
  No error edge → today's behaviour is unchanged.
- Retries first: the error edge is taken only once `MaxStepAttempts` is reached — a transient failure
  still retries. (A caught step is `Failed` in the timeline; the run continues down the error lane.)

## Failure semantics

In `ExecuteStepHandler`, the "out of retries" branch (after `stepExecution.Fail(error)`) checks for an
outgoing `"error"` edge from the current step:

- **Error edge present:** record the step `Failed`, persist its error for templating, and route down
  the error edge via `AdvanceExecution` with the chosen label `"error"`. The execution is **not**
  failed here; it settles on the error lane's terminal state (success → `Succeeded`; an unhandled
  failure on the error lane → `Failed`, unless it too has an error edge).
- **No error edge:** unchanged — `continueOnFailure` lets sibling lanes finish (settles `Failed`), or
  halt fails the execution immediately.

Routing (`WorkflowRouter.Route`) is extended so a failed step routes as if it "chose" `"error"`: it
takes `"error"` edges only and **skips** the success successors (and anything reachable only through
them), reusing the existing skip computation. `Readiness` is unaffected — a caught step is still
`Failed`, so a *success*-side join over it still skips per today's rule, while the error lane's own
joins behave normally.

## Error payload (templating)

A caught step's failure must be addressable on the error lane. Add a `steps.<key>.error` root:

- `TemplateContext` gains `StepErrors` (order → error JSON), populated in `BuildTemplateContextAsync`
  from `Failed` step executions, alongside the existing `StepOutputs` (succeeded only).
- The resolver adds a `steps.<id>.error[.field]` branch (numeric order or key, same as `output`),
  resolving to `{ "message": "...", "type": "...", "step": "<key>" }`. Messages are already
  secret-masked at failure time (`SecretMasker`), so nothing secret leaks into the error lane.

So an error handler can do, e.g., `message = "Deploy failed: {{steps.deploy.error.message}}"`.

## Validation

- `"error"` is a **reserved** edge label: a step may have **at most one** error edge (v1 — a single
  catch target; fan-out can come later), and `"error"` cannot be used as a `switch` case label.
- Enforced in `CreateWorkflow`/`UpdateWorkflow`'s edge validation (`BuildEdges`), alongside the
  existing out-of-range check.

## Surfaces

- **Engine:** `WorkflowRouter.Route` error mode + reserved label constant (`Edges.ErrorLabel`);
  `ExecuteStepHandler` failure path routes the error edge; `AdvanceExecution` carries the `"error"`
  label through to routing.
- **Templating:** `TemplateContext.StepErrors`; resolver `steps.<id>.error` branch.
- **Persistence:** none — `WorkflowEdge` already stores labels.
- **API:** reserved-label + one-error-edge validation in `BuildEdges`; `GetWorkflow` already returns
  edge labels.
- **Builder:** an "on error →" affordance from a step; the error edge drawn in a distinct colour
  (red), separate from switch-case edges; the inserter/validation already understand `{{steps.…}}` —
  extend ref validation to know `error` is a valid sub-root for upstream steps on the error lane.

## Tests

- Router: a failed step with an `"error"` edge routes to the error target and skips success
  successors; with no `"error"` edge, routing is unchanged; `"error"` takes no `"default"` fallback.
- Engine: step fails after retries → error lane runs → execution `Succeeded`; error lane itself fails
  with no catch → execution `Failed`; error edge beats `continueOnFailure`; transient failure still
  retries before the edge is taken.
- Templating: `{{steps.<key>.error.message}}` resolves on the error lane; secret-masked.
- Validation: two error edges from one step rejected; `"error"` as a switch case rejected.
