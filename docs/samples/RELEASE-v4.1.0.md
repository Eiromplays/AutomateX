# AutomateX v4.1.0

A tighter authoring loop: preview and test a single step without running the whole workflow.

## Highlights

- **Per-step preview.** Open **Preview / test** on any step in the builder, optionally paste a sample
  context, and see the step's current (even unsaved) config fully resolved — with every unresolved
  reference flagged at once and connection values masked. Zero side effects.
- **Run for real.** When you want to confirm the actual call works, run that one leaf action once
  against live connections and see its output or error. Opt-in, confirm-gated, editor-only, audited
  (`step.test`), and refused for control-flow nodes. No execution rows, chaining, retries, or
  idempotency — a raw single call.

## Upgrade notes

- **No migration.** New endpoints (`POST /api/workflows/{id}/preview-step` and `/test-step`) and
  builder UI only.
- Preview resolves connections live to know their field names but never returns secret values; a real
  run uses real connection values to make the call, and masks secrets in any surfaced error.

See the [per-step preview recipe](docs/recipes/per-step-preview.md). Full history:
[CHANGELOG.md](CHANGELOG.md).

---

*Next on the roadmap: workflow variables / environments, then a template gallery, then plugin
operations (logs/console, status, restart, resource limits), then AutomateX-as-MCP-server.*
