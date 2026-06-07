import type { ReactNode } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Link } from "react-router";
import { api, type ExecutionSummary } from "../lib/api";
import { StatusBadge } from "../components/status-badge";
import { toast } from "../components/toast";
import { useEngineEvents } from "../lib/use-engine-events";

// Chains render as trees: root execution first, chained children nested beneath,
// recursively. A child whose parent fell outside the page renders as a root.
function buildTree(executions: ExecutionSummary[]) {
  const ids = new Set(executions.map((x) => x.id));
  const children = new Map<string, ExecutionSummary[]>();
  const roots: ExecutionSummary[] = [];

  for (const execution of executions) {
    if (execution.parentExecutionId && ids.has(execution.parentExecutionId)) {
      const list = children.get(execution.parentExecutionId) ?? [];
      list.push(execution);
      children.set(execution.parentExecutionId, list);
    } else {
      roots.push(execution);
    }
  }

  // Children chronologically (the chain reads downward), roots newest-first.
  for (const list of children.values()) {
    list.sort((a, b) => a.startedAt.localeCompare(b.startedAt));
  }

  return { roots, children };
}

export default function Executions() {
  const queryClient = useQueryClient();
  const { data: executions, isLoading } = useQuery({
    queryKey: ["executions"],
    queryFn: api.executions.list,
  });

  useEngineEvents(() => queryClient.invalidateQueries({ queryKey: ["executions"] }));

  const remove = useMutation({
    mutationFn: (id: string) => api.executions.remove(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["executions"] });
      toast.success("Execution deleted.");
    },
    onError: (error) => toast.error(`Delete failed — ${String(error)}`),
  });

  const { roots, children } = buildTree(executions ?? []);

  const renderRow = (execution: ExecutionSummary, depth: number): ReactNode => (
    <li key={execution.id}>
      <div
        className="flex items-center hover:bg-zinc-900"
        style={depth > 0 ? { paddingLeft: `${depth * 1.5}rem` } : undefined}
      >
        <Link
          to={`/executions/${execution.id}`}
          className="flex flex-1 items-center justify-between px-4 py-3 text-sm"
        >
          <div className="flex items-center gap-3">
            {depth > 0 && <span className="text-violet-400/70">↳</span>}
            <StatusBadge status={execution.status} />
            <span className="font-medium text-zinc-200">{execution.workflowName}</span>
            {execution.triggeredBy === "workflow" ? (
              depth === 0 && (
                <span
                  className="rounded-full border border-violet-500/40 bg-violet-500/10 px-1.5 py-0.5 text-[10px] text-violet-400"
                  title="Started by another workflow (parent outside this page)"
                >
                  ⛓ chained
                </span>
              )
            ) : (
              <span className="text-xs text-zinc-500">{execution.triggeredBy}</span>
            )}
          </div>
          <span className="text-xs text-zinc-500">
            {new Date(execution.startedAt).toLocaleString()}
          </span>
        </Link>
        {(execution.status === "Succeeded" || execution.status === "Failed") && (
          <button
            type="button"
            title="Delete execution"
            onClick={() => {
              if (window.confirm("Delete this execution and its step history?")) {
                remove.mutate(execution.id);
              }
            }}
            className="px-3 py-3 text-xs text-zinc-600 hover:text-red-400"
          >
            ✕
          </button>
        )}
      </div>
      {(children.get(execution.id) ?? []).length > 0 && (
        <ul>{children.get(execution.id)!.map((child) => renderRow(child, depth + 1))}</ul>
      )}
    </li>
  );

  return (
    <div>
      <h1 className="mb-6 text-lg font-semibold">
        Executions <span className="ml-2 text-xs font-normal text-emerald-400">● live</span>
      </h1>

      {isLoading && <p className="text-sm text-zinc-500">Loading…</p>}

      <ul className="divide-y divide-zinc-800 rounded-lg border border-zinc-800">
        {roots.map((execution) => renderRow(execution, 0))}
        {executions?.length === 0 && (
          <li className="px-4 py-6 text-center text-sm text-zinc-500">No executions yet.</li>
        )}
      </ul>
    </div>
  );
}
