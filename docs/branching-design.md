# Branching / lanes — design proposal

**Status:** proposal for buy-in · **Date:** June 2026 · the v3.x "branching beyond gate" arc (v3-plan §7).

## Goal

Move workflows from a strictly **linear** sequence to a **graph**: conditional forks
("if X run these, else those"), and eventually parallel lanes and re-converging paths.
Today the `gate` is the only non-linear primitive and it only *halts*; this is the *fork*.

## Where we are (the constraint)

A workflow version is an ordered list of `WorkflowStep`s. The engine is message-driven,
one step at a time: after a step succeeds it cascades `ExecuteStep(next)` where **next =
the smallest `Order` greater than the current** (`ExecuteStep.cs`). The gate "branches" by
marking *every* higher-order step `Skipped` and completing. So "next step" is the single
place that needs to change — from *order-based* to *graph-based*.

## The model decision (needs your call)

### Option A — explicit-edge DAG (recommended)

Steps stay **nodes** (keep `Order` as a stable per-version node id + default layout). Add
**edges**: `WorkflowEdge(FromStepOrder, ToStepOrder, Condition?)`. 

- **Linear** workflows have *no stored edges* — the engine falls back to order-next, so
  every existing workflow keeps working untouched (backward compatible, no data migration
  for existing rows; just a new optional `WorkflowEdges` table).
- A **fork** is a step with multiple outgoing edges, each carrying a condition (the same
  `equals`/`notEquals`/`contains`/`isTruthy` vocabulary the gate already uses, evaluated
  against `{{steps.N.output…}}`). The engine follows the matching edge(s).
- **Parallel** (later) = a step with multiple *unconditional* edges → fan out (the cascade
  already supports returning several messages).
- **Merge** (later) = a node with multiple incoming edges → join.

Why recommended: it's the industry-standard model (n8n/Make/Zapier), and **our builder is
already a React Flow node graph** — edges are the natural unit there. The engine change is
conceptually clean: "compute next from edges, fall back to order."

### Option B — nested branch node

A `switch` step whose config *embeds* its lanes (`then: [...]`, `else: [...]`). Keeps the
top-level linear; branches are self-contained sub-sequences that converge after the node.
Simpler join semantics (implicit convergence), but it fights the graph builder (lanes
aren't real nodes/edges) and doesn't generalize to free-form parallel/merge.

**Recommendation: Option A** — it matches the builder we already have and is the real model.

## Execution semantics (Option A)

- **Next step:** after a step succeeds, find its outgoing edges. No edges → fall back to
  order-next (linear). Edges present → evaluate each edge's condition; cascade the first
  match (Phase 1) / all matches (Phase 3 parallel). An edge with no condition is "always".
- **Skipping:** steps reachable *only* through a not-taken edge are recorded `Skipped`
  (graph reachability from the taken frontier — generalises today's "skip the rest").
- **Completion:** the execution Succeeds when no active path has a next step (Phase 1: one
  active path; later: all lanes terminal).
- **Determinism preserved:** conditions are pure template comparisons; no AI, fully
  auditable — same spirit as the gate.

## Phased plan

1. **Phase 1 — conditional fork (no merge/parallel).** Edges + condition eval + reachability
   skipping. Delivers "if/else / switch": one path taken, the rest skipped, execution
   Succeeds. Engine + data model + tests. *Start here.*
2. **Phase 2 — merge / re-convergence.** A node with multiple incoming edges resumes the
   shared tail (join: continue once the active path arrives).
3. **Phase 3 — parallel lanes.** Fan-out on multiple unconditional edges + join; execution
   completes when all lanes are terminal. (Touches the cascade/completion accounting.)

## Touch list (Phase 1)

- **Data:** `WorkflowEdge` entity + migration; `AddVersion` accepts edges; export/import carry them.
- **Engine:** replace order-next with edge-next + fallback; condition eval (reuse `GateEvaluator`);
  reachability-based skipping. Tests-first on the routing/skip rules (pure where possible).
- **Builder:** user-drawn edges + per-edge condition (the React Flow canvas already has the nodes);
  trigger→lane routing (the note you flagged) folds in here.
- **Inspector/timeline:** the existing timeline already handles skipped steps — branches show as
  taken vs skipped.

## Open questions for you

1. **Model:** Option A (edge-DAG, recommended) or Option B (nested branch node)?
2. **Conditions:** on the **edges** (any step can fork; conditions live on outgoing edges) or on a
   dedicated **`switch` node** (one node type owns the branching, cleaner builder affordance)?
3. **Phase-1 scope:** confirm we start with *conditional fork only* (no merge/parallel yet)?
