# AutomateX v3.4.0

Workflows compose now. v3.4 adds calling one workflow from another and looping over a list.

## Highlights

- **`workflow.call` — sub-workflows.** Run another workflow as a step and wait for it. The parent
  suspends, the child runs, and the child's result becomes the call step's output
  (`{status, executionId, output}`) — branch on `{{steps.<key>.output.status}}`. It reuses the durable
  suspend/resume from v3.3, so a paused parent survives restarts. Workspace-isolated, depth-guarded
  (`MaxChainDepth`), and the execution page links parent ↔ child both ways. A failed child comes back
  as `status: "Failed"` data (pair with an error edge to treat it as a thrown failure).
- **`forEach` — loops.** Map a workflow over an array; each item becomes the child's
  `{{trigger.payload}}`, and the results are collected in order as the step output. **Sequential** in
  this release, with a durable per-item accumulator; an empty array short-circuits to `[]`.

Builder editors ship for both (a workflow picker, plus an items field for `forEach`). See the
[sub-workflows & loops recipe](docs/recipes/sub-workflows-and-loops.md).

## Upgrade notes

- **Migration `AddForEachState`** — adds the `ForEachStates` table and a nullable `ParentItemIndex`
  column. Applies on startup by default (`Database__MigrateOnStartup`); back up Postgres first. No
  backfill, no breaking changes — `workflow.call`/`forEach` are opt-in.

Full history: [CHANGELOG.md](CHANGELOG.md).

---

*Planned follow-up: a concurrency cap for `forEach` (run N items at once) — the launch side is ready;
it adds a row-locked accumulator for concurrent completions.*
