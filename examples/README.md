# Example workflows

Importable AutomateX workflow documents (format `automatex: 1`). Import one from the app via
**Workflows → Import**, then review the prefilled builder and create it. Documents carry no
secrets — connections travel as name references, so you create the matching connections yourself.

## api-uptime-watchdog.automatex.json

A single workflow that exercises the whole engine: scheduling, an HTTP probe, conditional
branching, an LLM step, true parallel lanes that join, and continue-on-failure.

### Topology

```
            (cron */5)
                │
        0 Probe health endpoint        http.request
                │
        1 Healthy?                      switch on statusCode
          ┌─────┴───────── default ──────────┐
        up│                                   │
        2 Heartbeat OK                3 Diagnose failure      llm.prompt
          (pushover, quiet)                   │  fan-out
                                       ┌───────┴───────┐
                               4 Page on-call   5 Log incident
                                 (pushover)       (http POST)
                                       └───────┬───────┘  join
                                       6 Incident recorded
                                         (pushover)
```

When the probe returns 200 the switch takes the `up` edge: a quiet heartbeat goes out and the
whole incident subtree is skipped. Otherwise it takes `default`: an LLM diagnoses the failure,
then step 3 fans out into two concurrent lanes (page on-call + log the incident) that rejoin at
step 6.

### Features it demonstrates

Scheduling via a cron trigger; `http.request` with `failOnErrorStatus` so the workflow — not the
HTTP client — decides what "down" means; a `switch` routing on `{{steps.0.output.statusCode}}`
with reachability-skip of the untaken branch; cross-step templating
(`{{steps.0.output.body}}`, `{{steps.3.output.text}}`) and connection references; true parallel
fan-out and a join; and `continueOnFailure: true` — if the incident log (step 5) fails, the
on-call page (step 4) still fires and the run settles `Failed` rather than aborting mid-incident.

### Connections to create first

Two connections, named exactly as referenced in the configs:

- **`pushover`** — `appToken`, `userKey`
- **`openai`** — `apiKey` (any OpenAI-compatible endpoint; set `baseUrl` in the `llm.prompt` step
  for Ollama/LM Studio/etc.)

### Trying each path

Out of the box the probe URL is `https://api.github.com/zen`, which returns 200 — so a run takes
the quiet **up** lane. To see the incident lanes, point the probe at a failing URL (e.g.
`https://httpbin.org/status/503`); the switch falls to `default` and the parallel page/log lanes
run. To watch **continue-on-failure**, set the *Log incident* URL to one that errors (e.g.
`https://httpbin.org/status/500`): step 5 fails, step 4 still pages, the join is skipped, and the
execution ends `Failed`.

> Webhook triggers aren't portable (their config holds a per-trigger secret), so they're omitted
> from the document. Add one in the UI after import if you want manual/HTTP-triggered runs.
