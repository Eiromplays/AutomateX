# Recipe: natural-language reminders

"Remind me in 2 hours to check the deploy" — typed into Matrix, parsed by a local
LLM, scheduled durably, and delivered on time. The Jarvis capstone.

```
you: "remind me in 2h to check the deploy"
  └─ matrix.onMessage ─► llm.prompt (extract delaySeconds + message as JSON)
                           └─ schedule.workflow (durably queue the reminder)
                                                  ⌛ … 2 hours later …
                                                  └─► "send-reminder" workflow ─► matrix.send
```

Two workflows: a tiny **deliverer** and a **parser** that schedules it.

## 1. The deliverer — "send-reminder"

One step, `matrix.send`, replying with whatever payload it's handed:

```json
{
  "homeserverUrl": "https://matrix-client.matrix.org",
  "accessToken": "{{connections.matrix.accessToken}}",
  "roomId": "{{trigger.payload.roomId}}",
  "msgType": "m.notice",
  "message": "⏰ Reminder: {{trigger.payload.message}}"
}
```

Note its workflow id — the parser needs it. (It needs no trigger; it only ever runs scheduled.)

## 2. The parser — "reminder-parser"

Trigger: **`matrix.onMessage`** (your room). Two steps:

**Step 0 — `llm.prompt`** extracts structured intent. Ask for strict JSON:

```json
{
  "baseUrl": "http://localhost:11434",
  "model": "qwen2.5:3b",
  "system": "Extract a reminder from the user's message. Reply with ONLY compact JSON: {\"delaySeconds\": <int>, \"message\": <string>}. 'in 2 hours' = 7200, 'tomorrow' = 86400. No prose.",
  "prompt": "{{trigger.payload.body}}"
}
```

**Step 1 — `schedule.workflow`** queues the deliverer. The LLM's JSON text is in
`{{steps.0.output.text}}`; since whole-token templates keep their JSON type, the
parsed fields flow straight in:

```json
{
  "workflowId": "<send-reminder workflow id>",
  "delaySeconds": {{steps.0.output.text.delaySeconds}},
  "payload": "{\"roomId\":\"{{trigger.payload.roomId}}\",\"message\":\"{{steps.0.output.text.message}}\"}"
}
```

That's it. Say "remind me in 90 seconds to stretch" and 90 seconds later the bot
pings you back — the scheduled run is durable, so it survives an API restart in
between.

## Why it's safe and clean

- **Durable**: `schedule.workflow` rides Wolverine's persisted scheduler — a restart
  mid-wait doesn't drop the reminder.
- **One-shot**: scheduled messages fire once and vanish. No reminder workflow to
  delete, no accumulating cron rows.
- **Workspace-bound**: a parser can only schedule workflows in its own workspace.
- **Auditable**: every reminder is an execution with `triggeredBy: scheduled` —
  the whole "I asked → it scheduled → it fired" story is in the executions list.
- **General**: nothing here is reminder-specific. The same two-step shape powers
  "every deploy, wait 10 min then health-check" or any *do X later* automation.

If the LLM occasionally returns sloppy JSON, the `schedule.workflow` step fails with
a clear template/parse error and the normal retry ladder applies — tighten the
system prompt and it settles.
