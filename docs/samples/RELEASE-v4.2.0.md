# AutomateX v4.2.0

Reusable, environment-aware configuration: workflow variables.

## Highlights

- **Variables.** Define a value once and reference it as `{{vars.<name>}}` across steps and workflows —
  workspace-wide, or per-workflow as an override. No more hard-coding the same base URL or channel into
  every step.
- **Environments.** Each variable holds a value per environment (`default`, `staging`, `prod`, …). Set
  the workspace's **active environment** and every workflow resolves to that set — flip staging→prod
  without editing a single workflow. A run can override the environment for a one-off, and the
  environment it used is stamped on the execution.
- **Secret variables.** Mark a variable secret and its values are encrypted at rest (workspace DEK) and
  masked everywhere, just like connection secrets. Plain values stay visible.
- **Builder integration.** A **Variables** page to manage it all; `{{vars.x}}` in the builder
  autocomplete; per-step preview resolves vars (secrets masked) and a real single-step run uses live
  values.

## Upgrade notes

- **Migrations:** adds `WorkspaceEnvironments`, `Variables`, `VariableValues`, a workspace
  active-environment column, and an execution environment stamp. Additive — existing workflows with no
  variables are unaffected; a workspace gets its `default` environment on first use of the page.
- **Secrets** use the same per-workspace DEK as connections — back up `Encryption__Key` as always.
- **Export/import:** workflows carry `{{vars.x}}` as **name references** (like connections), not
  values. The importing instance needs same-named variables/environments. (A future enhancement could
  travel plain values; for now variables are destination-supplied config, mirroring connections.)

See the [variables recipe](docs/recipes/variables.md) and
[design note](docs/workflow-variables-design.md). Full history: [CHANGELOG.md](CHANGELOG.md).

---

*Next on the roadmap: a template gallery, then plugin operations (logs/console, status, restart,
resource limits), then AutomateX-as-MCP-server.*
