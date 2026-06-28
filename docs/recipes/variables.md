# Recipe: variables & environments

Stop hard-coding the same base URL, region, or channel into every step — and stop editing workflows to
switch between staging and prod. Define a **variable** once and reference it as `{{vars.<name>}}`; give
it a different value per **environment**.

## Define a variable

On the **Variables** page:

1. (Optional) add environments — `default` exists already; add `staging`, `prod`, etc.
2. Add a variable, e.g. `baseUrl`. Tick **Secret** if it's sensitive (a shared token) — secret values
   are encrypted and write-only; plain values are shown.
3. Fill in a value per environment. A variable with a value only in `default` falls back to it for
   environments you haven't filled.

## Use it

In any step config, reference it like a connection:

```
{{vars.baseUrl}}/orders/{{trigger.payload.id}}
```

`{{vars.x}}` is in the builder autocomplete next to `{{connections…}}` and `{{steps…}}`. Per-step
**Preview** shows the resolved value (secrets masked); **Run for real** uses the live value.

## Switch environments

The **active environment** (set on the Variables page) is what runs use by default. Point it at `prod`
and every workflow resolves `{{vars.baseUrl}}` to the prod value — no workflow edits. A run can also
override the environment for a one-off (e.g. test against `staging`); the environment it used is
recorded on the execution.

## Workspace vs workflow scope

Workspace variables are shared by every workflow. A **workflow variable** of the same name overrides
the workspace one for that workflow only — handy for a per-workflow exception without forking the
shared value.

## Portability

Exported workflows carry `{{vars.x}}` as **name references** (like `{{connections.x}}`), not values.
The importing instance needs same-named variables/environments — set them up there, secrets included.

## Tips

- An unresolved `{{vars.x}}` (no value for the active environment, no `default` fallback) fails the
  step with a clear error; preview lists it up front.
- Keep genuine credentials in **connections** when they belong to a specific service; use secret
  variables for shared, non-connection tokens (a shared API key several steps reuse).
