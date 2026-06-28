# Workflow variables & environments (v4.2)

Today every configurable value lives inline in a step's config, and the only shared, referenceable
store is **connections** (which are credential bundles). There's no place for "the base URL of our
API", "the region", or "the Slack channel for alerts" — values you reuse across steps and workflows
and that differ between **staging** and **prod**. This adds:

- **Variables** — named, reusable values referenced as `{{vars.<name>}}`, defined at the **workspace**
  level (shared by every workflow) and optionally **overridden per workflow**.
- **Environments** — named value sets (e.g. `default`, `staging`, `prod`). Each variable holds a value
  *per environment*; the active environment selects which one a run sees.
- **Secret variables** — a variable can be marked secret: encrypted at rest (workspace DEK, like
  connections) and masked everywhere. Plain variables are stored and shown in clear.

## Model

```
Environment   (WorkspaceId, Name)                       -- e.g. default / staging / prod
Variable      (WorkspaceId, WorkflowId?, Name, Secret)  -- WorkflowId null = workspace scope
VariableValue (VariableId, EnvironmentId, Value)        -- per-environment value; encrypted if Secret
```

- A **workspace** variable (`WorkflowId == null`) is visible to all its workflows. A **workflow**
  variable (`WorkflowId` set) belongs to one workflow and **shadows** a workspace variable of the same
  name. So `{{vars.region}}` resolves workflow-first, then workspace.
- Every workspace starts with a `default` environment; you can't delete the last one.
- `Secret` is fixed per variable (all its env values are secret or none are). Secret values are sealed
  with the workspace DEK via `TenantCipher`; plain values are stored as-is so they stay exportable.

## Choosing the environment for a run

A run resolves against exactly one environment, chosen in this order:

1. **Per-execution override** — a manual execute or a trigger may name an environment
   (`RunWorkflow.Environment`); recorded on the execution.
2. **Workspace active environment** — `Workspace.ActiveEnvironmentId` (operator-set; defaults to
   `default`).
3. Fallback to `default`.

The resolved environment **name is stamped on the `Execution`** so history shows which set a run used,
and a replay re-runs in the same environment by default. A missing value for the chosen environment
falls back to `default`'s value; still missing → the ref is unresolved (fails the step, like any bad
ref).

## Resolution

A new templating root, mirroring `connections`:

```
{{vars.region}}            the value of `region` for this run's environment + scope
{{vars.apiBaseUrl}}        plain → inlined; secret → inlined at execution, masked in output
```

At execution, the engine builds a `Variables` map (name → value) once per run:

1. Pick the environment (above).
2. Load workspace variables, then overlay the workflow's own (workflow wins on name collision).
3. For each, take the env value (or `default` fallback); decrypt secret values via `TenantCipher`;
   add secret values to the existing `SecretSink` so they're masked in outputs/errors exactly like
   connection secrets.

`TemplateContext` gains an optional `Variables` dictionary; `ResolveRoot` handles the `vars` root and
marks secret-variable reads for masking. Preview (v4.1) resolves `vars` too — plain values shown,
secret values masked (`******`), same as connections.

## API

Workspace-scoped, editor for values, owner for structure (create/delete environments):

```
GET/POST/DELETE  /api/environments                     list / create / delete (not the last)
PUT              /api/workspace/active-environment      { environmentId }
GET              /api/variables                         workspace + (optional ?workflowId=) variables
POST/PUT/DELETE  /api/variables[/{id}]                  declare / rename / set-secret / remove
PUT              /api/variables/{id}/values             { environmentId, value } (encrypted if secret)
```

Reads never return secret values — only names, the secret flag, and which environments have a value
set (mirrors how connections expose key names, not secrets).

## UI

A **Variables** page under the workspace (alongside Connections): an environment switcher + "active"
selector, and a grid of variables × environments. A secret toggle per variable; secret cells are
write-only (masked, "set/replace"). In the builder, `{{vars.<name>}}` joins the existing
autocomplete next to `{{connections…}}` and `{{steps…}}`.

## Export / import

A workflow export already excludes secrets and references connections by name. Variables follow suit:
**plain workflow variables travel** with the export; **secret variables and all workspace variables**
are emitted as name references, to be supplied in the destination workspace. Import validates that
referenced variable names exist (warn, don't hard-fail — same posture as connection refs).

## Testing

- Resolution precedence: workflow var shadows workspace var; env value vs `default` fallback; unknown
  var → unresolved.
- Secret values: encrypted at rest; decrypt on resolve; added to `SecretSink` → masked in output and
  errors; never returned by the read API.
- Environment selection: per-execution override > workspace active > `default`; env name stamped on
  the execution; replay reuses it.
- `vars` root in the templating tests (whole-string keeps type; interpolation; preview masking).

## Risks

- **Secret sprawl.** Variables become a second secret store. Mitigate: same cipher + masking + DEK as
  connections; reads never expose values; audited like other config mutations.
- **Environment confusion.** "Which env did this run use?" must be answerable — hence stamping the env
  on the execution and showing it in the run detail.
- **Migration surface.** Three new tables + a workspace column + a `RunWorkflow`/`Execution` field.
  Additive (minor release); existing workflows with no variables are unaffected.

## Slicing

- **v4.2a** — this design note.
- **v4.2b** — data model (3 tables + workspace active-env column) + migration + secret-value crypto +
  the pure resolution core (env + scope → name/value map). Tests-first.
- **v4.2c** — engine wiring: `vars` template root + per-run variables context (env choice,
  per-execution override, masking) + env stamped on the execution. Tests-first.
- **v4.2d** — API: environments + variables + values CRUD + active-environment, validation.
- **v4.2e** — UI: the Variables page + builder `{{vars.x}}` autocomplete.
- **v4.2f** — export/import handling + wrap-up docs (CHANGELOG, recipe, release notes).
