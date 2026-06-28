# Recipe: preview and test a single step

Stop saving the whole workflow and running it just to check one step. In the builder, every step has a
**Preview / test** panel — open it from the step card (form mode) or the selected node (canvas).

## Preview (no side effects)

1. Open **Preview / test** on the step.
2. Optionally paste a **sample context** — the data the step's templates resolve against:

   ```json
   {
     "triggerPayload": { "email": "a@b.com" },
     "stepOutputs": { "fetch": { "id": 42 } }
   }
   ```

   `triggerPayload` feeds `{{trigger.payload…}}`; `stepOutputs` is keyed by step key (or numeric
   order) and feeds `{{steps.<key>.output…}}`.
3. Click **Preview config**. You get:
   - the **resolved config** with your live (unsaved) edits applied,
   - any **unresolved** references, listed as red chips (e.g. `steps.fetch.output.id` when no sample
     value was supplied), shown inline as `[unresolved: …]` placeholders,
   - which **connection fields** the step reads — values are always masked (`******`), never shown.

Preview never executes the action and never reveals a secret.

## Run for real (opt-in)

When you actually want to confirm the call works — an SMTP send, a webhook — click **Run for real**.
It executes that one action once with the resolved config and **real** connection values, then shows
the output or the error. It does not create an execution, chain, retry, or apply idempotency.

- It asks for confirmation first — a real run has real side effects.
- It's editor-only and recorded in the audit log as `step.test`.
- Control-flow steps (`switch`, `forEach`, `wait`, `workflow.call`) can't be run on their own — the
  button is disabled for them; use Preview to check their config instead.

## Tips

- Previewing an unsaved edit is the point — tweak the config, preview, repeat, then save once it's
  right.
- An unresolved `{{steps.…}}` ref just means your sample context didn't include that upstream output —
  add it under `stepOutputs`, or run the real workflow once and copy the value in.
