# Recipe: Jarvis-lite — talk to your homelab

A conversational loop on your own hardware: a Matrix message wakes a workflow,
a local LLM reads it, the reply lands back in the room. No cloud anywhere.

```
you type in Element ──► matrix.onMessage (sync long-poll listener)
                          └─► llm.prompt (Ollama, local)
                                └─► matrix.send (reply to the same room)
```

## Prerequisites

- The Matrix + Llm plugins installed (catalog or `dotnet publish` into `plugins/`).
- A `matrix` connection with the bot's `accessToken` (see the self-deploy recipe for bot setup).
- Ollama running with a model pulled (`ollama pull qwen2.5:3b`).
- A room the bot has joined (unencrypted).

## The workflow

Create "jarvis-lite" with two steps:

1. **`llm.prompt`**
   ```json
   {
     "baseUrl": "http://localhost:11434",
     "model": "qwen2.5:3b",
     "system": "You are a terse homelab assistant called AutomateX. One short paragraph, no fluff.",
     "prompt": "{{trigger.payload.sender}} says: {{trigger.payload.body}}"
   }
   ```
2. **`matrix.send`** — replying into the room the message came from:
   ```json
   {
     "homeserverUrl": "https://matrix-client.matrix.org",
     "accessToken": "{{connections.matrix.accessToken}}",
     "roomId": "{{trigger.payload.roomId}}",
     "msgType": "m.notice",
     "message": "{{steps.0.output.text}}"
   }
   ```

Then add the trigger — type **Matrix: On Message** (`matrix.onMessage`):

```json
{
  "homeserverUrl": "https://matrix-client.matrix.org",
  "accessToken": "{{connections.matrix.accessToken}}",
  "roomId": "!yourroom:matrix.org"
}
```

Trigger configs resolve `{{connections.…}}` at listener start — the stored trigger
(and the UI) only ever show the template.

## Why it can't run away

The bot's **own messages never fire the trigger** (loop protection is unconditional),
so the reply in step 2 doesn't re-trigger the workflow. History before the listener
starts is skipped, so restarts don't replay the room. And if the LLM or homeserver
hiccups, the normal retry ladder and the listener's backoff handle it — check the
execution list for the conversation's audit trail, lineage and all.

Leave `roomId` out of the trigger to listen everywhere the bot is joined — then the
reply step's `{{trigger.payload.roomId}}` makes it answer wherever it was spoken to.
