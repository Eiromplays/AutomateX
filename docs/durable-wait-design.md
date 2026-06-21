# Durable wait / human approval

**Status:** proposed (v3.3) · the keystone control-flow primitive. Grows from the durable scheduler
(`schedule.workflow` → `bus.ScheduleAsync`) and the execution state machine
(`ExecuteStepHandler`/`AdvanceExecution`).

## Problem

The engine runs a workflow start-to-finish in one go. Real processes need to **pause**: wait for a
human to approve, wait until a time, or wait for an external signal — then resume where they left
off, possibly hours or days later. The engine is already durable (Postgres-backed messages,
crash-resume), so a paused run should survive restarts for free. This is also the primitive that v3.3
**retry-from-a-step** and the roadmap's **synchronous sub-workflow call** reuse.

## Decision

Add a **`wait`** step that suspends the execution until it's resumed — by a timer or an external
signal — and a new execution status **`Waiting`**.

- `wait` is engine-recognized (like `gate`/`switch`): `ExecuteStepHandler` special-cases it instead
  of running it as a normal action.
- Config (one mode):
  - `delay` — `delaySeconds` / `until` (resume at a time).
  - `signal` — wait for an external resume (approval), with optional `timeoutSeconds`.
- On hitting a `wait` step the handler: records the step `Waiting`, sets the execution `Waiting`,
  schedules a wake for the deadline (if any) via `bus.ScheduleAsync`, persists a resume token (for
  signal waits), and returns **without advancing**.
- Resume — by timer wake or external call — completes the wait step (its output carries the resume
  payload / decision), sets the execution `Running`, and advances from the wait step normally.

## State machine

```
Running --hit wait--> Waiting --resume(signal|timer)--> Running --> … --> Succeeded/Failed
```

- New `ExecutionStatus.Waiting` (and a `Waiting` step status). `Waiting` is non-terminal but
  not-running: `AdvanceExecution` settlement treats it as "don't settle," and the
  **`StuckExecutionSweeper` must not touch it** (it only sweeps `Running` past a deadline — `Waiting`
  is intentional, so it's naturally excluded; assert this in a test).
- A wait step's terminal transition (`Waiting → Succeeded`) happens only on resume.

## Resume

A new message **`ResumeExecution(ExecutionId, StepOrder, Reason, Payload)`**:

- **Timer:** scheduled at the deadline when the wait is entered. `Reason = "timeout"` (signal mode) or
  `"timer"` (delay mode).
- **Signal:** published by `POST /api/executions/{id}/resume` (authenticated, Editor) or a signed
  single-use **approval link** (HMAC token, reusing `WebhookSecret`-style signing). `Reason =
  "resumed"`, `Payload` = the approve/reject decision + any data.
- **Idempotency / race:** a guarded UPDATE claims the transition (`Waiting → Running` only if still
  `Waiting` and the wait step not yet completed). The timer and a signal can both fire; the first
  wins, the second is a no-op. Mirrors the existing atomic-claim pattern in `AdvanceExecution`.

The wait step's output is the resume payload, e.g. `{ "reason": "resumed", "decision": "approve",
"by": "alice", "data": {…} }`, so a downstream `gate`/`switch` can branch on
`{{steps.<key>.output.decision}}` — approvals are just data.

## Persistence

A small **`ExecutionWait`** row per active wait: `ExecutionId`, `StepOrder`, `Token` (for signal
resume; compared fixed-time, never returned), `ExpiresAt?`. Cleared on resume. Migration
`AddExecutionWait`. (The execution/step `Waiting` statuses are stored in the existing string status
columns — no migration there.)

## Approval links

For human-in-the-loop, the `wait` (signal) step can surface an **approval URL** the workflow sends
(via `discord.send`/`email.send`/etc.): `…/api/executions/{id}/resume/{token}?decision=approve`.
The token is single-use and resolves to the resume signal. The authenticated UI button is the other
path (an Approve/Reject control on the execution detail page for `Waiting` runs).

## Parallel caveat (v1)

The execution status is single-valued, so a `wait` is meant for a **sequential** path. A wait inside
one parallel lane while sibling lanes run concurrently is a documented footgun for v1 (the builder can
warn; the engine doesn't police it), mirroring the trigger-entry footgun. The common cases —
approval gate on the main flow, "wait 24h then continue" — are fully supported.

## Surfaces

- **Engine:** `ExecutionStatus.Waiting`; `wait` action recognized in `ExecuteStepHandler`;
  `ResumeExecution` handler (claim + advance); scheduler wake via `bus.ScheduleAsync`; sweeper
  excludes `Waiting`.
- **Persistence:** `ExecutionWait` (token/deadline) + migration `AddExecutionWait`.
- **API:** `POST /api/executions/{id}/resume` (authed) + signed approval-link route; resume payload →
  wait step output.
- **Builder/UI:** `wait` step editor (mode + delay/until/timeout); `Waiting` status colour; an
  Approve/Reject (resume) affordance on the execution detail page.

## Tests

- Engine: a `delay` wait suspends then resumes at the timer and continues; a `signal` wait resumes on
  the message and continues; the resume payload is the wait step's output; a timeout takes the timeout
  path; resume of an already-resumed/again execution is idempotent (timer+signal race → one wins).
- Sweeper: a `Waiting` execution past the stuck-deadline is **not** failed.
- API: resume requires auth/valid token; an invalid/used token is rejected.
