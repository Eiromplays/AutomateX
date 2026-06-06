import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { useNavigate, useParams } from "react-router";
import { api } from "../lib/api";

const inputClass =
  "rounded-md border border-zinc-700 bg-zinc-900 px-3 py-1.5 text-sm " +
  "placeholder:text-zinc-600 focus:border-emerald-500 focus:outline-none";

export default function WorkflowDetail() {
  const { id = "" } = useParams();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [triggerType, setTriggerType] = useState("cron");
  const [cron, setCron] = useState("*/5 * * * *");

  const { data: workflow, isLoading } = useQuery({
    queryKey: ["workflow", id],
    queryFn: () => api.workflows.get(id),
  });

  const execute = useMutation({
    mutationFn: () => api.workflows.execute(id),
    onSuccess: ({ executionId }) => navigate(`/executions/${executionId}`),
  });

  const addTrigger = useMutation({
    mutationFn: () =>
      api.triggers.create(id, {
        type: triggerType,
        config: triggerType === "cron" ? { cron } : {},
      }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["workflow", id] }),
  });

  const removeTrigger = useMutation({
    mutationFn: (triggerId: string) => api.triggers.remove(triggerId),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["workflow", id] }),
  });

  if (isLoading || !workflow) return <p className="text-sm text-zinc-500">Loading…</p>;

  return (
    <div className="space-y-8">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-lg font-semibold">{workflow.name}</h1>
          {workflow.description && <p className="text-sm text-zinc-500">{workflow.description}</p>}
        </div>
        <button
          type="button"
          onClick={() => execute.mutate()}
          disabled={execute.isPending}
          className="rounded-md bg-emerald-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-emerald-500 disabled:opacity-50"
        >
          {execute.isPending ? "Starting…" : "Run now"}
        </button>
      </div>

      <section>
        <h2 className="mb-3 text-sm font-medium text-zinc-300">
          Steps <span className="text-zinc-500">(v{workflow.latestVersion.version})</span>
        </h2>
        <ol className="space-y-2">
          {workflow.latestVersion.steps.map((step) => (
            <li key={step.id} className="rounded-lg border border-zinc-800 px-4 py-3">
              <div className="flex items-center gap-3">
                <span className="text-xs text-zinc-500">#{step.order + 1}</span>
                <span className="text-sm font-medium">{step.name ?? step.actionType}</span>
                <span className="rounded bg-zinc-800 px-1.5 py-0.5 text-xs text-zinc-400">
                  {step.actionType}
                </span>
              </div>
              <pre className="mt-2 overflow-x-auto rounded bg-zinc-900 p-2 text-xs text-zinc-400">
                {JSON.stringify(JSON.parse(step.configJson), null, 2)}
              </pre>
            </li>
          ))}
        </ol>
      </section>

      <section>
        <h2 className="mb-3 text-sm font-medium text-zinc-300">Triggers</h2>
        <ul className="mb-4 space-y-2">
          {workflow.triggers.map((trigger) => (
            <li
              key={trigger.id}
              className="flex items-center justify-between rounded-lg border border-zinc-800 px-4 py-2.5 text-sm"
            >
              <div className="flex items-center gap-3">
                <span className="font-medium">{trigger.type}</span>
                {!trigger.enabled && <span className="text-xs text-red-400">disabled</span>}
                {trigger.type === "webhook" && (
                  <code className="text-xs text-zinc-500">POST /api/webhooks/{trigger.id}</code>
                )}
                {trigger.nextRunAt && (
                  <span className="text-xs text-zinc-500">
                    next: {new Date(trigger.nextRunAt).toLocaleString()}
                  </span>
                )}
              </div>
              <button
                type="button"
                onClick={() => removeTrigger.mutate(trigger.id)}
                className="text-xs text-zinc-500 hover:text-red-400"
              >
                Delete
              </button>
            </li>
          ))}
          {workflow.triggers.length === 0 && (
            <li className="text-sm text-zinc-500">No triggers — runs manually only.</li>
          )}
        </ul>

        <div className="flex items-center gap-2">
          <select
            className={inputClass}
            value={triggerType}
            onChange={(e) => setTriggerType(e.target.value)}
          >
            <option value="cron">cron</option>
            <option value="webhook">webhook</option>
          </select>
          {triggerType === "cron" && (
            <input
              className={`${inputClass} font-mono`}
              value={cron}
              onChange={(e) => setCron(e.target.value)}
              placeholder="*/5 * * * *"
            />
          )}
          <button
            type="button"
            onClick={() => addTrigger.mutate()}
            disabled={addTrigger.isPending}
            className="rounded-md border border-zinc-700 px-3 py-1.5 text-sm hover:bg-zinc-900 disabled:opacity-50"
          >
            Add trigger
          </button>
        </div>
        {addTrigger.error && <p className="mt-2 text-sm text-red-400">{String(addTrigger.error)}</p>}
      </section>
    </div>
  );
}
