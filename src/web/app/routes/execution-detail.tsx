import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Link, useNavigate, useParams } from "react-router";
import { api, type ExecutionDetail as ExecutionDetailData } from "../lib/api";
import { SourceBadge } from "../components/action-source";
import { StatusBadge } from "../components/status-badge";
import { toast } from "../components/toast";
import { useEngineEvents } from "../lib/use-engine-events";

export default function ExecutionDetail() {
  const { id = "" } = useParams();
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const remove = useMutation({
    mutationFn: () => api.executions.remove(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["executions"] });
      toast.success("Execution deleted.");
      navigate("/executions");
    },
    onError: (error) => toast.error(`Delete failed — ${String(error)}`),
  });

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
        <div className="flex items-center gap-4">
          {(execution.status === "Succeeded" || execution.status === "Failed") && (
            <button
              type="button"
              onClick={() => {
                if (window.confirm("Delete this execution and its step history?")) {
                  remove.mutate();
                }
              }}
              disabled={remove.isPending}
              className="text-sm text-zinc-500 hover:text-red-400 disabled:opacity-50"
            >
              Delete
            </button>
          )}
          <Link to={`/workflows/${execution.workflowId}`} className="text-sm text-zinc-400 hover:text-zinc-100">
            View workflow →
          </Link>
        </div>
      </div>
      {remove.error && <p className="text-sm text-red-400">{String(remove.error)}</p>}

      <ChainLineage execution={execution} />

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
              <SourceBadge actionType={step.actionType} />
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

// Upstream lineage: parsed from the trigger payload the chained execution carries.
function ChainLineage({ execution }: { execution: ExecutionDetailData }) {
  const lineage = (() => {
    if (execution.triggeredBy !== "workflow" || !execution.triggerPayload) return null;
    try {
      const payload = JSON.parse(execution.triggerPayload) as {
        chainDepth?: number;
        source?: { executionId?: string; status?: string };
      };
      return payload.source?.executionId
        ? { ...payload.source, depth: payload.chainDepth ?? 1 }
        : null;
    } catch {
      return null;
    }
  })();

  if (!lineage && execution.chained.length === 0) return null;

  return (
    <div className="space-y-1 rounded-md border border-violet-500/30 bg-violet-500/5 px-3 py-2 text-sm">
      {lineage && (
        <p>
          ⛓ Chained from execution{" "}
          <Link to={`/executions/${lineage.executionId}`} className="text-violet-400 hover:underline">
            {lineage.executionId?.slice(0, 13)}…
          </Link>{" "}
          <span className="text-xs text-zinc-500">
            (source {lineage.status} · depth {lineage.depth})
          </span>
        </p>
      )}
      {execution.chained.map((child) => (
        <p key={child.executionId}>
          ⛓ Chained into execution{" "}
          <Link to={`/executions/${child.executionId}`} className="text-violet-400 hover:underline">
            {child.executionId.slice(0, 13)}…
          </Link>{" "}
          <span className="text-xs text-zinc-500">({child.status})</span>
        </p>
      ))}
    </div>
  );
}
