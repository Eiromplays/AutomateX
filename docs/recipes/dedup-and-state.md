# Recipe: dedup & durable state (KV actions)

Every workflow has a durable, per-workflow key/value store. Four built-in actions read and write
it, so a workflow can remember things across runs — "have I already handled this?", "when did I
last notify?", "what was the previous value?" — without an external database.

| Action | Config | Output |
| --- | --- | --- |
| `kv.get` | `key` | `found` (bool), `value` (string or null) |
| `kv.set` | `key`, `value`, `ttlSeconds?` | `ok` |
| `kv.setIfAbsent` | `key`, `value` (default `"1"`), `ttlSeconds?` | `acquired` (bool) |
| `kv.delete` | `key` | `removed` (bool) |

Notes:

- **Scope.** State is per workflow. Two workflows can use the same key without colliding, and keys
  are namespaced internally so they never clash with a trigger's own dedup state.
- **TTL.** `ttlSeconds` expires an entry; after it lapses the key reads as absent and `setIfAbsent`
  can claim it again. Omit it for a permanent entry.
- **Templating.** `key` and `value` take templates, e.g. `key: "deployed:{{trigger.payload.json.0.tag_name}}"`.

## The dedup primitive: `setIfAbsent` + `gate`

`kv.setIfAbsent` atomically claims a key: it returns `acquired = true` the first time and `false`
forever after (until any TTL lapses). On its own it changes nothing — pair it with a **gate** on
`acquired` so the rest of the workflow runs only the first time:

```
kv.setIfAbsent   key = "handled:{{trigger.payload.id}}"     → output.acquired
gate             value = {{steps.0.output.acquired}}, isTruthy = true
…rest of the workflow (runs once per id)…
```

A closed gate halts the run (later steps skip, the execution still succeeds), so a re-delivery of
the same event simply does nothing.

## Run once per release tag (self-deploy)

`http.poll` dedups on the *whole* response body, so any incidental change to the GitHub releases
feed (an asset's download count, a new pre-release) re-fires even when the newest tag is unchanged.
Claim the tag to make the deploy idempotent:

```
kv.setIfAbsent   key = "deployed:{{trigger.payload.json.0.tag_name}}"
gate             value = {{steps.0.output.acquired}}, isTruthy = true
ssh.command      pull + restart
```

The bundled **Self-deploy on new release** template already includes these two steps.

## Once per day (rate-limit a notification)

Use a TTL so the claim resets each day:

```
kv.setIfAbsent   key = "digest-sent", ttlSeconds = 86400
gate             value = {{steps.0.output.acquired}}, isTruthy = true
…send the digest…
```

## Remember the previous value (only act on change)

```
kv.get           key = "last-status"          → output.value
gate             value = {{steps.0.output.value}}, notEquals = {{trigger.payload.status}}
kv.set           key = "last-status", value = {{trigger.payload.status}}
…notify that the status changed…
```
