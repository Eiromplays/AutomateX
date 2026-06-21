# AutomateX v3.3.0

Runs that pause. v3.3 adds durable waits and human approvals — and the ability to retry a run from a
single step.

## Highlights

- **Durable wait / human approval.** A new `wait` step suspends a run into a **`Waiting`** status:
  - **Timed** — `delaySeconds` or `until`; the run resumes automatically on the timer.
  - **Approval (signal)** — `mode: "signal"` (with optional `timeoutSeconds`); the run parks until
    someone resumes it.
  - Paused runs are durable — they survive restarts (they ride the engine's scheduler).
  - The **resume payload becomes the wait step's output**, so a downstream `gate`/`switch` can branch
    on the decision — approvals are just data.
  - Resume from the UI (a **▶ Resume** button on the execution page) or
    `POST /api/executions/{id}/resume`.
- **Retry from a step.** Re-run a finished execution starting at a chosen step, reusing the earlier
  steps' outputs (pinned to that run's version). A **"↻ Retry from here"** action in the step
  inspector, or `POST /api/executions/{id}/retry-from/{order}`.

See the [approvals & waits recipe](docs/recipes/approvals-and-waits.md) and the
[durable-wait design](docs/durable-wait-design.md).

## Upgrade notes

- **No migration, no breaking changes.** The new `Waiting`/`Caught` step+execution statuses ride the
  existing text status columns. Existing workflows are unaffected; `wait` is opt-in.
- The stuck-execution sweeper leaves `Waiting` runs alone (they're intentionally paused).

Full history: [CHANGELOG.md](CHANGELOG.md).

---

*Planned follow-up: a signed, single-use email approval link (resume without signing in). Next in the
roadmap: v3.3's sibling arc — loops & sub-workflows — which reuse this suspend/resume primitive.*
