# Recipe: reshape with `transform`, deliver with `webhook.send`

Two built-in actions cover the common "take some JSON, shape it, post it somewhere signed" need
without an external script: **`transform`** (JMESPath) reshapes data between steps, and
**`webhook.send`** delivers a payload with an optional HMAC signature.

## `transform` — reshape/extract JSON

| Field | Meaning |
| --- | --- |
| `input` | The JSON to operate on — usually a whole-step reference, e.g. `{{steps.probe.output}}`. |
| `query` | A [JMESPath](https://jmespath.org) expression. |

The query result becomes the step output **directly**, so downstream references read the new shape
as `{{steps.<key>.output.<…>}}`.

```
transform   input = {{steps.fetch.output.body}}
            query = { count: length(items), ids: items[].id, failures: items[?!ok].id }
```

Given `{"items":[{"id":1,"ok":true},{"id":2,"ok":false}]}` the step output is:

```json
{ "count": 2, "ids": [1, 2], "failures": [2] }
```

Useful patterns:

- **Extract** — `release.tag_name`, or `items[0].id`.
- **Filter + project** — `items[?ok].id` keeps only matching elements.
- **Build a new object** — `{ tag: release.tag_name, by: release.author.login }`.
- **Functions** — `sort_by(items, &date)`, `length(items)`, `join(', ', names)`.

A malformed query fails the step immediately (it's an authoring error, not a transient one).

## `webhook.send` — deliver a (signed) payload

| Field | Meaning |
| --- | --- |
| `url` | Where to POST. |
| `body` | The payload (templated). Defaults to `application/json` — override with `contentType`. |
| `headers` | Optional extra request headers. |
| `signingSecret` | Optional. When set, the raw body is HMAC-SHA256 signed. |
| `signatureHeader` | Header to carry the signature (default `X-Webhook-Signature`). |

The signature format is `sha256=<lowercase hex>` — the same one AutomateX verifies on **inbound**
webhook triggers, so one instance can post to another and have it verify out of the box. For GitHub-
style receivers, set `signatureHeader` to `X-Hub-Signature-256`. It fails the step on a non-2xx
response, so a failed delivery surfaces (and retries) rather than passing silently.

```
webhook.send  url           = https://example.com/hooks/automatex
              body          = {{steps.shape.output}}
              signingSecret = {{connections.partner.webhookSecret}}
```

## Putting it together

Fetch → reshape → deliver signed, all by step name (stable across reorders):

```
http.request  "Fetch"   GET https://api.example.com/status
transform     "Shape"   input = {{steps.fetch.output.body}}
                        query = { up: items[?state=='up'].name, down: items[?state!='up'].name }
webhook.send  "Notify"  url = https://example.com/hooks/status
                        body = {{steps.shape.output}}
                        signingSecret = {{connections.partner.webhookSecret}}
```

Because references use step **keys** (`fetch`, `shape`), inserting or reordering steps later never
re-points them — the builder's reference inserter (the 🔗 button) lists upstream step outputs and
connections so you don't hand-type them.
