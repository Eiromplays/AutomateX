# Per-step test / dry-run (v4.1)

Authoring a step today is guess-and-run: you save the whole workflow, execute it, and read the
execution to see whether `{{steps.0.output.body.id}}` actually resolved and whether the action did what
you meant. This adds a tighter loop on a **single step**, in two phases with very different blast
radii:

1. **Preview** (default, zero side effects) — resolve the step's templated config against a sample (or
   borrowed) context and show the final config, which tokens resolved to what, what's still
   unresolved, and which connection fields it would read (masked). Nothing executes.
2. **Real run** (explicit, opt-in) — actually execute that one leaf action with the resolved config and
   show its output or error. No persistence, no chaining, no retries. Side effects are real, so it's
   gated behind a confirm.

Preview is the common case and the safe default; real run is there for "does the SMTP/Slack call
actually work" without building a throwaway workflow.

## Inputs

A preview/test targets a step in a specific workflow **version** (the draft being edited), identified
by step key (or order). The caller supplies a **context** to resolve against — two sources, either or
both:

- **Sample context** — a JSON blob the user types/pastes: `trigger.payload`, and optionally
  `steps.<key>.output` for upstream steps the target depends on.
- **Borrowed execution** — pick a past execution of this workflow; its real trigger payload and prior
  step outputs seed the context. Lets you preview against data you know flowed before.

Connections are always resolved live for the workspace (same `ConnectionResolver` path as a real run),
so `{{connections.x.y}}` reflects the actual stored secret — but values are **masked** in the
response.

## Preview — how it resolves

`TemplateResolver.Resolve` throws on the first unresolvable token (correct for execution: a bad ref
fails the step). Preview needs the opposite — collect *all* unresolved tokens and keep going. So:

- Add a tolerant resolution mode (a `TemplateContext` carrying a "collect, don't throw" flag, or a
  sibling `TryResolve` that returns `(resolvedJson, string[] unresolved)`). Unresolved tokens render as
  a placeholder (e.g. `[unresolved: steps.2.output.id]`) in the returned config so the shape stays
  valid JSON.
- Reuse `StepReferences` for the static ref list the step declares, so the response can separate
  "resolved", "unresolved (no value in context)", and "invalid (ref to a non-existent/later step)".
- The existing `SecretSink` on `TemplateContext` already records which connection fields a resolution
  touched — use it to report (masked) which secrets the step would read.

Preview output:

```
{
  "resolvedConfig": { ...final config JSON with placeholders for misses... },
  "resolved":   [ { "token": "trigger.payload.email", "value": "a@b.com" }, ... ],
  "unresolved": [ "steps.2.output.id" ],
  "connectionsUsed": [ { "name": "smtp", "fields": ["host","username","password"] } ]
}
```

## Real run — how it executes

Phase 2. After a preview, an explicit "Run this step for real":

- Resolve the config (strict — a real run with unresolved refs is an error, same as execution).
- Resolve the action executor: `ActionRegistry.Get(type, workspaceId)` and call `ExecuteAsync` once
  with a fresh `ActionContext` (its own `HttpClient`/logger, a synthetic execution id). Plugin actions
  therefore run out-of-process through the supervisor, exactly as in production — same isolation.
- **Bypass** the execution machinery entirely: no `Execution`/`StepRun` rows, no Wolverine enqueue, no
  chaining/`onFailure`, no retries, no idempotency, no metrics, no audit-as-execution. It's a raw
  single call. (Optionally emit one `audit` entry `step.test` so operators can see a real send
  happened — open question below.)
- Return `{ "ok": true, "output": {...} }` or `{ "ok": false, "error": "..." }`.

**Control-flow nodes are preview-only.** `switch`, `forEach`, `workflow.call`, `wait`, and the
fan-out/branch nodes don't have a meaningful single-call "output" and would mutate state — real run is
rejected for them with a clear message; preview (config resolution) still works.

## API

Scoped to a workflow version, editor role:

```
POST /api/workflows/{id}/versions/{versionId}/steps/{key}/preview
     body: { sampleContext?: {...}, borrowExecutionId?: guid }
     -> preview output above

POST /api/workflows/{id}/versions/{versionId}/steps/{key}/test
     body: { sampleContext?: {...}, borrowExecutionId?: guid, confirm: true }
     -> { ok, output | error }
```

`test` requires `confirm: true` and is refused for control-flow node types.

## Builder UX

A **Preview** button on the selected step opens a panel: a sample-context editor (prefilled from the
latest execution if one exists, via "borrow"), the resolved config with unresolved refs highlighted
(reuse the existing ref-validation chips), and the masked connection-fields list. A secondary **Run
for real** button (with a side-effects warning, disabled for control-flow nodes) shows the output
inline.

## Testing

- Tolerant resolver: resolved/unresolved partition; placeholder keeps JSON valid; whole-string token
  keeps type; `SecretSink` reports connection fields.
- Preview endpoint: sample context vs borrowed execution; missing upstream step → listed unresolved,
  200 not 500.
- Real run: a host/test action executes once and returns output; a control-flow type is rejected; no
  `Execution`/`StepRun` rows are written; unresolved ref → error before any call.

## Open questions

- **Audit a real test run?** Leaning yes — a `step.test` audit entry (actor + step + workflow), since a
  real send is a real side effect. Cheap and honest.
- **Borrowed-execution secrets.** Borrow only payload + step outputs, never re-inject decrypted
  connection values from history — connections always resolve live + masked.

## Risks

- Real run has real side effects (emails, webhooks). Mitigations: opt-in + `confirm`, editor-gated,
  side-effects warning in the UI, control-flow excluded, and (likely) audited.
- Tolerant resolution must not leak secrets via placeholders — unresolved tokens render the *path*,
  never a value, and connection values are masked everywhere in the response.
