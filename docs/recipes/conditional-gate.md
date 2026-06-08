# Recipe: conditional workflows with `gate`

The `gate` built-in stops a workflow unless a condition holds — the deterministic
"check-and-act-if" primitive, no AI required. A closed gate skips the remaining
steps and the execution still **succeeds** (stopping the chain is normal flow).

## How it works

Put a `gate` step between a check and the action it guards. The gate compares a
(usually templated) value:

- `equals` / `notEquals` — string compare
- `contains` — substring
- `isTruthy` — `true`/`false`, non-empty vs empty, `"0"`/`"null"` count as falsy
- no operator → opens when the value is truthy

Multiple operators are AND-ed.

## Example: only notify when something changed

```
http.request  → fetch a status endpoint
gate          → { "value": "{{steps.0.output.statusCode}}", "equals": "200" }
matrix.send   → "All good ✅"   (only runs when the gate is open)
```

If step 0 returns anything but 200, the gate closes: `matrix.send` is skipped and
the execution finishes Succeeded with the notify step marked **Skipped** in history.

## Example: act on an LLM yes/no

```
http.request  → fetch data
llm.prompt    → system: "Reply only true or false: is action needed?"
gate          → { "value": "{{steps.1.output.text}}", "isTruthy": true }
ssh.command   → run the remediation   (only when the model said it's needed)
```

## Notes

- Gates are deterministic and auditable — the gate step's output records the
  open/closed reason, and skipped steps show a grey **Skipped** badge.
- Because a gated execution still Succeeds, workflow chains watching "succeeds"
  fire normally — chain off "failed" if you only want the un-gated path to trigger.
- Pair gates with a `ticker`/`cron` trigger for "check every N minutes, act only
  if…" automations.
