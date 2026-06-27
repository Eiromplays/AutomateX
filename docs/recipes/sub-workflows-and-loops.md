# Recipe: sub-workflows & loops

Compose workflows: call one from another and wait for the result, or run one per item of a list.

## Call a workflow (`workflow.call`)

Run another workflow as a step and use its result:

```
workflow.call   workflowId = <pick a workflow>
                payload    = {{steps.fetch.output.order}}   # optional → child's {{trigger.payload}}
```

The run **pauses** at this step until the called workflow finishes, then continues. The step output
is the child's result:

```json
{ "status": "Succeeded", "executionId": "…", "output": <child's last step output> }
```

Branch on it with a following `gate`/`switch`:

```
workflow.call  "fulfil"
switch         value = {{steps.fulfil.output.status}}
               case "Succeeded" → notify-ok
               default          → notify-failed
```

- The child must live in the same workspace. Recursion is capped by `Engine__MaxChainDepth`.
- A failed child comes back as `status: "Failed"` data (the call step still succeeds) — gate on it,
  or put an error edge on the call step if you'd rather treat it as a thrown failure.
- The execution page links parent ↔ child both ways.

## Loop over a list (`forEach`)

Run a workflow once per array item and collect the results:

```
forEach   items      = {{steps.fetch.output.rows}}   # an array
          workflowId = <pick a workflow>
```

Each item becomes that run's `{{trigger.payload}}`. The step output is the array of child results, in
item order:

```json
[ { "status": "Succeeded", "output": … }, { "status": "Succeeded", "output": … } ]
```

- Runs **sequentially** in this release (one item at a time); an empty array completes instantly.
- Per-item failures land as `status: "Failed"` in that slot — inspect the results array downstream
  (e.g. `transform` to filter failures).
- A concurrency cap (run N at once) is planned.

## Putting it together

Fetch a list, process each item through a sub-workflow, then summarise:

```
http.request  "fetch"    GET https://api.example.com/orders
forEach       "each"     items = {{steps.fetch.output.body.orders}}, workflowId = <fulfil-order>
transform     "summary"  input = {{steps.each.output}}
                         query = { ok: length([?status=='Succeeded']), failed: [?status=='Failed'] }
```
