import { useQuery } from "@tanstack/react-query";
import { Link } from "react-router";
import { api } from "../lib/api";

export default function Workflows() {
  const { data: workflows, isLoading, error } = useQuery({
    queryKey: ["workflows"],
    queryFn: api.workflows.list,
  });

  return (
    <div>
      <div className="mb-6 flex items-center justify-between">
        <h1 className="text-lg font-semibold">Workflows</h1>
        <Link
          to="/workflows/new"
          className="rounded-md bg-emerald-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-emerald-500"
        >
          New workflow
        </Link>
      </div>

      {isLoading && <p className="text-sm text-zinc-500">Loading…</p>}
      {error && <p className="text-sm text-red-400">{String(error)}</p>}

      <ul className="divide-y divide-zinc-800 rounded-lg border border-zinc-800">
        {workflows?.map((workflow) => (
          <li key={workflow.id}>
            <Link
              to={`/workflows/${workflow.id}`}
              className="flex items-center justify-between px-4 py-3 hover:bg-zinc-900"
            >
              <div>
                <div className="text-sm font-medium">{workflow.name}</div>
                {workflow.description && (
                  <div className="text-xs text-zinc-500">{workflow.description}</div>
                )}
              </div>
              <span className="text-xs text-zinc-500">v{workflow.latestVersion}</span>
            </Link>
          </li>
        ))}
        {workflows?.length === 0 && (
          <li className="px-4 py-6 text-center text-sm text-zinc-500">
            No workflows yet — create the first one.
          </li>
        )}
      </ul>
    </div>
  );
}
