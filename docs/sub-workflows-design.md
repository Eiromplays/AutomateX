# Sub-workflows & loops

**Status:** proposed (v3.4) · builds directly on the durable-wait suspend/resume keystone
(`docs/durable-wait-design.md`) and the chaining cascade (`WorkflowChaining`).

## Problem

Workflows can't compose. `schedule.workflow` and `workflow` triggers fire another workflow
**fire-and-forget**; nothing waits for the result. Real automation wants to **call** a workflow as a
step (get its output back) and to **loop** a sub-flow over a list. Both reduce to one primitive: run
a child workflow and resume the parent when it finishes — which the v3.3 wait machinery already does
for timers and approvals.

## Decision

Add **`workflow.call`** — a synchronous sub-workflow step — first; **`forEach`** is then "map
`workflow.call` over an array."

### `workflow.call`

Engine-recognized (like `wait`/`gate`). Config: `{ workflowId, payload? }`.

- On hitting the step: validate the target (exists, **same workspace**, depth under
  `Engine__MaxChainDepth`), start a child `RunWorkflow` carrying a **parent link**, and **suspend the
  parent** (reuse `Waiting`). The child runs independently on the durable engine.
- When the child reaches a terminal state, the parent is **resumed** with the child's result as the
  call step's output.

### Parent link

The child needs to know who to wake. Add to `Execution`:

- `ParentExecutionId : Guid?`, `ParentStepOrder : int?` — set when the child is started. Migration
  `AddSubWorkflowLink`.
- `RunWorkflow` gains optional `ParentExecutionId`/`ParentStepOrder`; `RunWorkflowHandler` stamps them
  onto `Execution.Start`.

### Resuming the parent (durable)

Every terminal site already calls `WorkflowChaining.CollectAsync` and cascades the result through the
outbox. Add a sibling: when a just-terminal execution has a `ParentExecutionId`, cascade a
`ResumeExecution(parentId, parentStepOrder, "child", childResult)` alongside the chain messages. This
rides the same durable outbox — a finishing child wakes its parent crash-safely, with no best-effort
event listener in the critical path.

`ResumeExecution`'s atomic `Waiting → Running` claim (v3.3) already makes the wake idempotent.

### Child result shape

The parent's call step output:

```json
{ "status": "Succeeded", "executionId": "…", "output": <child's last step output, if any> }
```

`status`/`executionId` are always present; `output` is the highest-order succeeded step's output
(best-effort — a workflow has no single return value). A downstream `gate`/`switch` branches on
`{{steps.<key>.output.status}}`; data flows via `{{steps.<key>.output.output…}}`.

### Guards

- **Workspace isolation** — the target must live in the caller's workspace (as chaining enforces).
- **Recursion/depth** — carry depth on the parent link (or reuse the chain-depth payload field) and
  refuse past `MaxChainDepth`, so a workflow that calls itself can't run away.
- **Failure** — a failed child resumes the parent with `status: "Failed"`; pair with an **error edge**
  (v3.2) on the call step if you want the parent to treat it as a thrown failure instead of data.

## `forEach` (loops)

A `forEach` step maps a sub-workflow over an array, with a concurrency cap, collecting ordered
results.

- **Model:** dynamic fan-out — one child execution per item (reusing the `workflow.call` parent-link +
  resume machinery), a durable per-parent **pending counter / result slots**, and a join that resumes
  the parent once every item is terminal, with results gathered into an array
  (`{{steps.<key>.output}}` = `[…]`).
- **Persistence:** a small `ForEachState` (or reuse step output as accumulator) tracking item count,
  completed count, and per-index results — updated atomically as each child finishes (guarded UPDATE,
  like the join claim).
- **Concurrency cap:** start up to N children at once; as each finishes, launch the next pending item.
- **Hard part:** the durable, dynamic fan-out/join is the most involved engine work in v3.x — the
  static fan-out/join from branching is the template. Tests-first; cap item count defensively.

**Sequencing:** ship `workflow.call` first (self-contained, high value). `forEach` follows once the
call + resume path is proven; it may land as its own point release if the join needs more bake time.

## Surfaces

- **Engine:** `workflow.call` recognized in `ExecuteStepHandler` (start child + suspend);
  `Execution.ParentExecutionId/ParentStepOrder`; `RunWorkflow` parent fields; parent-resume cascade at
  the terminal sites; depth/workspace guards.
- **Persistence:** `AddSubWorkflowLink` migration (+ `ForEachState` for loops).
- **API/Builder:** a `workflow.call` editor (target workflow picker + payload); parent↔child lineage
  on the execution detail page (the child links back, like retry lineage).

## Tests

- `workflow.call`: parent suspends and resumes with the child's result on success; a failed child
  resumes with `status: Failed` (and routes the call step's error edge if present); a missing/
  cross-workspace target fails the step; self-call past `MaxChainDepth` is refused; resume is
  idempotent (redelivery).
- `forEach`: N items → N child runs → ordered results; a failing item respects continue-on-failure vs
  halt; concurrency cap honoured; empty array completes immediately.
