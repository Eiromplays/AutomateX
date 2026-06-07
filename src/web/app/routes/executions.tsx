import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Link } from "react-router";
import { api } from "../lib/api";
import { StatusBadge } from "../components/status-badge";
import { useEngineEvents } from "../lib/use-engine-events";

export default function Executions() {
  const queryClient = useQueryClient();
  const { data: executions, isLoading } = useQuery({
    queryKey: ["executions"],
    queryFn: api.executions.list,
  });

  useEngineEvents(() => queryClient.invalidateQueries({ queryKey: ["executions"] }));

  const remove = useMutation({
    mutationFn: (id: string) => api.executions.remove(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["executions"] }),
  });

  return (
    <div>
      <h1 className="mb-6 text-lg font-semibold">
        Executions <span className="ml-2 text-xs font-normal text-emerald-400">● live</span>
      </h1>

      {isLoading && <p className="text-sm text-zinc-500">Loading…</p>}

      <ul className="divide-y divide-zinc-800 rounded-lg border border-zinc-800">
        {executions?.map((execution) => (
          <li key={execution.id} className="flex items-center hover:bg-zinc-900">
            <Link
              to={`/executions/${execution.id}`}
              className="flex flex-1 items-center justify-between px-4 py-3 text-sm"
            >
              <div className="flex items-center gap-3">
                <StatusBadge status={execution.status} />
                <span className="text-zinc-400">{execution.triggeredBy}</span>
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
          </li>
        ))}
        {executions?.length === 0 && (
          <li className="px-4 py-6 text-center text-sm text-zinc-500">No executions yet.</li>
        )}
      </ul>
    </div>
  );
}
