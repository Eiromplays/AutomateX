import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { api, type WorkflowDetail } from "../lib/api";
import { toast } from "./toast";

const inputClass =
  "w-full rounded-md border border-zinc-700 bg-zinc-900 px-3 py-1.5 text-sm placeholder:text-zinc-600 focus:border-emerald-500 focus:outline-none";

// Expose a workflow as an MCP tool + show how to connect a client. The server itself is gated by
// Engine__AllowMcpServer + an API key; this panel just manages the per-workflow opt-in.
export function WorkflowMcpPanel({ workflow }: { workflow: WorkflowDetail }) {
  const queryClient = useQueryClient();
  const [exposed, setExposed] = useState(workflow.mcpExposed);
  const [toolName, setToolName] = useState(workflow.mcpToolName ?? "");
  const [description, setDescription] = useState(workflow.mcpDescription ?? "");

  const save = useMutation({
    mutationFn: () =>
      api.workflows.setMcp(workflow.id, { exposed, toolName: toolName || null, description: description || null }),
    onSuccess: (result) => {
      queryClient.invalidateQueries({ queryKey: ["workflow", workflow.id] });
      toast.success(exposed ? `Exposed as MCP tool “${result.resolvedToolName}”.` : "MCP tool removed.");
    },
    onError: (error) => toast.error(String(error)),
  });

  const endpoint = `${typeof window !== "undefined" ? window.location.origin : ""}/api/mcp`;
  const config = `POST ${endpoint}
  X-Api-Key: <your API key>
  X-Workspace-Id: <workspace id>`;

  return (
    <section className="space-y-3 rounded-lg border border-zinc-800 p-4">
      <h2 className="text-sm font-medium text-zinc-300">MCP tool</h2>
      <p className="text-xs text-zinc-500">
        Expose this workflow as a tool for MCP clients (Claude, agents). They call it with a{" "}
        <code className="text-emerald-400">payload</code> and get the final step&apos;s output back.
      </p>

      <label className="flex items-center gap-2 text-sm text-zinc-300">
        <input
          type="checkbox"
          className="accent-emerald-500"
          checked={exposed}
          onChange={(e) => setExposed(e.target.checked)}
        />
        Expose as MCP tool
      </label>

      {exposed && (
        <div className="space-y-2">
          <label className="block text-xs text-zinc-400">
            Tool name (optional — defaults to a slug of the workflow name)
            <input
              className={`${inputClass} mt-1`}
              placeholder="summarize_ticket"
              value={toolName}
              onChange={(e) => setToolName(e.target.value)}
            />
          </label>
          <label className="block text-xs text-zinc-400">
            Description (what the model reads to decide whether to call it)
            <textarea
              className={`${inputClass} mt-1`}
              rows={2}
              placeholder="Summarise a support ticket and post it to Slack."
              value={description}
              onChange={(e) => setDescription(e.target.value)}
            />
          </label>
        </div>
      )}

      <button
        type="button"
        disabled={save.isPending}
        onClick={() => save.mutate()}
        className="rounded-md bg-emerald-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-emerald-500 disabled:opacity-50"
      >
        {save.isPending ? "Saving…" : "Save"}
      </button>

      {workflow.mcpExposed && (
        <div className="space-y-1">
          <span className="text-xs text-zinc-500">Connect a client to:</span>
          <pre className="overflow-x-auto rounded-md border border-zinc-800 bg-zinc-950 p-2 font-mono text-[11px] text-zinc-300">
            {config}
          </pre>
          <p className="text-[11px] text-zinc-600">
            Requires <code>Engine__AllowMcpServer=true</code> and a configured API key on the server.
          </p>
        </div>
      )}
    </section>
  );
}
