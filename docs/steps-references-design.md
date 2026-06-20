# Named step references

**Status:** proposed (v3.1) · extends the templating resolver
(`src/AutomateX/Engine/Templating/TemplateResolver.cs`) and the connection-ref validation shipped in
rc.9.

## Problem

Step outputs are referenced positionally: `{{steps.<order>.output.x}}`. Order is the step's index in
the version's step list, so **inserting or reordering a step silently re-points every downstream
reference**. Real failure: adding `kv.setIfAbsent` + `gate` dedup in front of a deploy pushed the
`ssh.command` step from order 0 to 2, and a `matrix.send` still reading `{{steps.0.output.stdout}}`
resolved to the kv step — `segment 'stdout' not found`.

Names exist (`StepDefinition.Name`) but are **optional and not unique**, so they can't be a safe
reference key as-is. We also want, where feasible, output-field autocomplete and static validation
that flags broken refs at authoring time instead of at run time.

## Decision

Add a stable, human-readable **step key** as the reference identity. Keep positional refs working.

- `StepDefinition` gains `Key : string` — a slug auto-derived from the first `Name`
  (`SSH deploy` → `ssh-deploy`), editable, **unique within a workflow version**. The display `Name`
  stays cosmetic and freely editable; the `Key` is what references bind to, so renaming the step
  does not break references and reordering never touches them.
- Templating accepts `{{steps.<key>.output.…}}` **and** the existing `{{steps.<order>.output.…}}`
  (back-compat). Numeric segments resolve by order as today; non-numeric segments resolve by key.

Neither index (fragile on reorder) nor display name (fragile on rename) is robust alone — a stable
key derived from the name gives readability *and* stability, and keeps export/import documents
hand-editable.

## Resolver

`ResolveRoot`'s `steps` case currently requires `int.TryParse(segments[1], …)`. Extend:

- numeric `segments[1]` → resolve via `StepOutputs[order]` (unchanged).
- non-numeric → resolve `order` from a new `IReadOnlyDictionary<string,int> StepKeys` on
  `TemplateContext`, then `StepOutputs[order]`. Unknown key throws the same
  `TemplateResolutionException` shape (`unknown step '<key>'`, list known keys).

`StepKeys` is built once per execution from the version's steps. ~15 lines plus the map; no change to
`StepOutputs` (still keyed by order — the engine's source of truth).

## Validation

Reuse the connection-ref validator (rc.9 D). Parse every `{{…}}` in step configs at save **and** live
in the builder, classifying each `steps.*` ref:

- **resolves** — known key / in-range order, field path valid against the upstream result schema
  (green).
- **fragile** — positional `steps.<order>` ref; resolves, but warn "index-based, convert to a name?"
  (amber).
- **unknown step** — key not present / order out of range (error).
- **out-of-DAG** — references a step not topologically before this one (error; it can't have run).
- **unknown field** — key resolves but the trailing path doesn't exist in the upstream result schema,
  *when that schema is closed* (error); otherwise unverifiable (see below).

Create/Update reject hard errors; the builder surfaces all classes inline. A one-shot "rewrite indexes
→ keys" action migrates existing workflows.

## Type-aware autocomplete

Action result types are already exported as JSON Schema via `GET /api/actions`. Drive autocomplete
from that:

- `{{steps.` → suggest only **upstream-reachable** steps (respects the DAG, so you can't reference a
  step that runs after you), by key, showing the display name.
- `…<key>.output.` → suggest fields from that step's result schema (`exitCode`/`stdout`/`stderr` for
  `ssh.command`, etc.).

**Graceful downgrade (the "warn when not feasible" case):** when a result schema is open-ended —
`additionalProperties`, a raw `http.request` JSON body, or a plugin that declares no result schema —
field knowledge stops at `.output`. Autocomplete offers nothing past it and validation cannot verify
the trailing path, so the field-level check downgrades to a soft note rather than a false error. This
is schema-driven static checking, not CLR typing; dynamic JSON stays addressable but unverified.

## Surfaces

- **SDK/persistence:** `StepDefinition.Key`; `WorkflowStep.Key` + unique index per version; migration
  `AddStepKey` backfilling slugs from existing names (dedup with `-2`, `-3` suffixes).
- **Engine:** `TemplateContext.StepKeys`; resolver `steps.<key>` branch.
- **API:** create/update accept/return `key` per step; `GetWorkflow` exposes it; key uniqueness +
  ref validation in the request validators.
- **Builder:** key field (auto-slug from name, editable, dup-checked); extend the existing
  `{{…}}` autocomplete to `steps.<key>.output.<field>`; ref chips reuse the connection-ref colors.

## Back-compat & rollout

Positional refs never stop working, so nothing breaks on upgrade. Phased, each tests-first:

1. **Key + resolver** — add the column/slug backfill and the `steps.<key>` resolver branch
   (low-risk, invisible until used).
2. **Validation** — classify refs at save + in the builder; offer index→key rewrite.
3. **Autocomplete** — schema-driven step + output-field suggestions with the open-schema downgrade.

## Tests

- Resolver: `steps.<key>.output.x` resolves; unknown key throws with known-keys hint; numeric refs
  unchanged; rename keeps key-refs working; reorder keeps key-refs working (and would break the old
  index-ref — the regression we're closing).
- Validation: unknown-step and out-of-DAG refs rejected; positional ref flagged fragile; unknown
  field rejected only under a closed result schema, allowed under an open one.
- Migration: slug backfill is unique per version and deterministic.
