import { useQuery, useQueryClient } from "@tanstack/react-query";
import { Link, useParams } from "react-router";
import { api } from "../lib/api";
import { StatusBadge } from "../components/status-badge";
import { useEngineEvents } from "../lib/use-engine-events";

export default function ExecutionDetail() {
  const { id = "" } = useParams();
  const queryClient = useQueryClient();

  const { data: execution, isLoading, error } = useQuery({
    queryKey: ["execution", id],
    queryFn: () => api.executions.get(id),
    retry: false,
  });

  useEngineEvents((engineEvent) => {
    if (engineEvent.payload.executionId === id) {
      queryClient.invalidateQueries({ queryKey: ["execution", id] });
    }
  });

  if (isLoading) return <p className="text-sm text-zinc-500">Loading…</p>;
  if (error || !execution) {
    return (
      <p className="text-sm text-zinc-500">
        Execution not found in this workspace —{" "}
        <a href="/executions" className="text-emerald-400 hover:underline">
          back to executions
        </a>
        .
      </p>
    );
  }

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-3">
          <h1 className="text-lg font-semibold">Execution</h1>
          <StatusBadge status={execution.status} />
          <span className="text-xs font-normal text-emerald-400">● live</span>
        </div>
        <Link to={`/workflows/${execution.workflowId}`} className="text-sm text-zinc-400 hover:text-zinc-100">
          View workflow →
        </Link>
      </div>

      <dl className="grid grid-cols-2 gap-3 text-sm sm:grid-cols-4">
        <div>
          <dt className="text-xs text-zinc-500">Triggered by</dt>
          <dd>{execution.triggeredBy}</dd>
        </div>
        <div>
          <dt className="text-xs text-zinc-500">Started</dt>
          <dd>{new Date(execution.startedAt).toLocaleString()}</dd>
        </div>
        <div>
          <dt className="text-xs text-zinc-500">Completed</dt>
          <dd>{execution.completedAt ? new Date(execution.completedAt).toLocaleString() : "—"}</dd>
        </div>
        <div>
          <dt className="text-xs text-zinc-500">Duration</dt>
          <dd>
            {execution.completedAt
              ? `${new Date(execution.completedAt).getTime() - new Date(execution.startedAt).getTime()} ms`
              : "—"}
          </dd>
        </div>
      </dl>

      <ol className="space-y-3">
        {execution.steps.map((step) => (
          <li key={step.id} className="rounded-lg border border-zinc-800 p-4">
            <div className="flex items-center gap-3">
              <span className="text-xs text-zinc-500">#{step.stepOrder + 1}</span>
              <span className="rounded bg-zinc-800 px-1.5 py-0.5 text-xs text-zinc-400">
                {step.actionType}
              </span>
              <StatusBadge status={step.status} />
              <span className="text-xs text-zinc-500">
                {step.attempts} attempt{step.attempts === 1 ? "" : "s"}
                {step.failedAttempts > 0 && ` (${step.failedAttempts} failed)`}
              </span>
            </div>
            {step.output && (
              <pre className="mt-3 overflow-x-auto rounded bg-zinc-900 p-2 text-xs text-zinc-400">
                {step.output}
              </pre>
            )}
            {step.error && (
              <pre className="mt-3 overflow-x-auto rounded bg-red-950/40 p-2 text-xs text-red-400">
                {step.error}
              </pre>
            )}
          </li>
        ))}
        {execution.steps.length === 0 && (
          <li className="text-sm text-zinc-500">No steps recorded.</li>
        )}
      </ol>
    </div>
  );
}
