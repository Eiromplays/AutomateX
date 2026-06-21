# Recipe: waits & human approvals

The `wait` step pauses a run until a timer fires or someone approves it. Because the engine is
durable, a paused run survives restarts — it can wait minutes or days.

## Timed wait

Pause, then continue automatically:

```
wait   delaySeconds = 3600     # or: until = 2026-06-01T09:00:00Z
```

The run goes `Waiting`, then resumes on the timer and carries on. The wait step's output records
`{"reason":"timer"}`.

## Human approval (signal wait)

Wait for an explicit resume instead of a timer:

```
wait   mode = signal           # optionally: timeoutSeconds = 86400
```

The run parks at `Waiting` until it's resumed:

- **From the UI** — open the execution and click **▶ Resume**.
- **From the API** — `POST /api/executions/{id}/resume` with an optional JSON body. That body becomes
  the wait step's output, so you can carry a decision:

  ```json
  { "decision": "approve", "by": "alice" }
  ```

If `timeoutSeconds` is set and nobody resumes in time, the run resumes itself with
`{"reason":"timeout"}` — branch on that to handle the no-response case.

## Branch on the decision

The resume payload is the wait step's output, so a following `gate` or `switch` routes on it:

```
notify-approver   discord.send  "Approve deploy? {{execution.id}}"
wait              mode = signal, timeoutSeconds = 86400
switch            value = {{steps.wait.output.decision}}
                  case "approve" → deploy
                  default        → notify-cancelled   # also catches the timeout
```

## Notes

- A `wait` is for a **sequential** path. Pausing one lane of a parallel fan-out while siblings run is
  a v1 footgun — keep waits on the main flow.
- Resuming an already-resumed (or finished) run is a no-op — a timer and a manual resume can race;
  the first wins.
- A signed, single-use email approval **link** (resume without logging in) is planned; today's resume
  is the authenticated UI/API call.
