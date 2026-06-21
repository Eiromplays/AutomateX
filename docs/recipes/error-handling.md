# Recipe: handle failures with error branches

By default a failed step either halts the run or — with continue-on-failure — lets sibling lanes
finish and settles the execution `Failed`. An **error edge** lets you *handle* the failure instead:
catch it, react (notify, clean up, fall back), and keep the run green.

## Author it

In the builder, each step has an **On error → step** picker. Point it at a handler step:

```
deploy           (on error → notify-failure)
notify-success   ← runs only when deploy succeeds
notify-failure   ← runs only when deploy fails (the error lane)
```

- The error edge is **additive**: the success path is unchanged. `deploy` still flows to
  `notify-success` on success; on failure it jumps to `notify-failure`.
- The error target is a **terminal lane head** off the main flow — nothing chains into it except the
  error edge, and it doesn't auto-continue. (Want it to rejoin? Fan it out to a later step.)
- The edge is drawn red/dashed in the graph.

## What "failure" means

The error edge is taken only after the step exhausts its retries — a transient blip still retries
first. The caught step is recorded **Caught** (orange in the timeline), distinct from a hard
`Failed`. Error handling takes precedence over both halt and continue-on-failure.

A caught failure is **not** an execution failure: the run settles on the error lane's own outcome —
`Succeeded` if the handler succeeds, `Failed` if the handler itself fails with nothing to catch it.

## Read the error

On the error lane, the failure is available as a template:

```
notify-failure  →  discord.send
                   content = "Deploy failed: {{steps.deploy.error.message}}"
```

`{{steps.<key>.error.message}}` is the (secret-masked) failure message of the caught step. Reference
the step by its **name/key**, so it survives renames and reorders.

## Notes

- One error edge per step (the `"error"` label is reserved).
- Combine with [transform](transform-and-webhooks.md) and the notification actions
  (`discord.send`, `slack.send`, `telegram.send`, `pushover.send`, `email.send`) to build real alert-
  on-failure flows.
