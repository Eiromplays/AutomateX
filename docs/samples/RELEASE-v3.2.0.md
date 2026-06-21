# AutomateX v3.2.0

Handle failures, don't just hit them. v3.2 opens the **control flow & resilience** arc with
try/catch error branches.

## Highlights

- **Try/catch error branches.** Give any step an **"on error → step"** target in the builder. If the
  step fails after its retries, the run jumps to that handler lane instead of failing — so you can
  notify, clean up, or fall back and carry on.
  - The error edge is **additive**: the success path is unchanged; the error target is a terminal
    lane head off the main flow, drawn red/dashed in the graph.
  - The caught step is recorded **Caught** (orange in the timeline), distinct from a hard failure. A
    caught failure does **not** fail the execution — the run settles on the error lane's outcome.
  - The failure is readable on the error lane as `{{steps.<key>.error.message}}` (secret-masked).
  - Error handling takes precedence over both halt and continue-on-failure.

See the [error-handling recipe](docs/recipes/error-handling.md) and the
[design note](docs/error-branches-design.md).

## Upgrade notes

- **No migration, no breaking changes.** `"error"` is a reserved edge label (like switch's
  `"default"`), so error branches ride the existing workflow-edge schema. Existing workflows are
  unaffected; the feature is opt-in per step.

Full history: [CHANGELOG.md](CHANGELOG.md).

---

*Next in the v3.2 control-flow arc (rolling into v3.3): durable wait / human approval and
retry-from-a-step.*
