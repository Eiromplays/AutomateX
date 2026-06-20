import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { Link, useNavigate } from "react-router";
import { toast } from "../components/toast";
import { ListSkeleton } from "../components/ui/skeleton";
import { api } from "../lib/api";

export default function Workflows() {
  const navigate = useNavigate();
  const {
    data: workflows,
    isLoading,
    error,
  } = useQuery({
    queryKey: ["workflows"],
    queryFn: api.workflows.list,
  });

  const [query, setQuery] = useState("");
  const needle = query.trim().toLowerCase();
  const filtered =
    workflows?.filter(
      (w) => w.name.toLowerCase().includes(needle) || (w.description ?? "").toLowerCase().includes(needle),
    ) ?? [];

  return (
    <div>
      <div className="mb-6 flex items-center justify-between">
        <h1 className="text-lg font-semibold">Workflows</h1>
        <div className="flex items-center gap-3">
          <label className="cursor-pointer text-sm text-zinc-500 hover:text-zinc-200">
            Import
            <input
              type="file"
              accept=".json"
              className="hidden"
              onChange={(e) => {
                const file = e.target.files?.[0];
                e.target.value = "";
                if (!file) return;
                file.text().then((text) => {
                  try {
                    const doc = JSON.parse(text) as { automatex?: number };
                    if (doc.automatex !== 1) {
                      toast.error("Import failed — not an AutomateX workflow export.");
                      return;
                    }
                    // Review-before-create: the builder opens prefilled so
                    // placeholders get filled before v1 exists.
                    navigate("/workflows/new", { state: { importDoc: doc } });
                  } catch {
                    toast.error("Import failed — that file is not valid JSON.");
                  }
                });
              }}
            />
          </label>
          <Link to="/templates" className="text-sm text-zinc-400 hover:text-zinc-100">
            Templates
          </Link>
          <Link
            to="/workflows/new"
            className="rounded-md bg-emerald-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-emerald-500"
          >
            New workflow
          </Link>
        </div>
      </div>

      {isLoading && <ListSkeleton />}
      {error && <p className="text-sm text-red-400">{String(error)}</p>}

      {(workflows?.length ?? 0) > 0 && (
        <input
          type="text"
          placeholder="Search workflows…"
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          className="mb-3 w-full max-w-sm rounded-md border border-zinc-700 bg-zinc-900 px-3 py-1.5 text-sm placeholder:text-zinc-600 focus:border-emerald-500 focus:outline-none"
        />
      )}

      {!isLoading && (
        <ul className="divide-y divide-zinc-800 rounded-lg border border-zinc-800">
          {filtered.map((workflow) => (
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
                  {workflow.runsAfter.length > 0 && (
                    <div className="text-xs text-violet-400">
                      ⛓ runs after {workflow.runsAfter.join(", ")}
                    </div>
                  )}
                  {workflow.feeds.length > 0 && (
                    <div className="text-xs text-violet-400">⛓ feeds {workflow.feeds.join(", ")}</div>
                  )}
                </div>
                <span className="flex items-center gap-2 text-xs text-zinc-500">
                  {!workflow.enabled && <span className="text-amber-400">⏸ paused</span>}v
                  {workflow.latestVersion}
                </span>
              </Link>
            </li>
          ))}
          {workflows?.length === 0 && (
            <li className="px-4 py-6 text-center text-sm text-zinc-500">
              No workflows yet — create the first one, or{" "}
              <Link to="/templates" className="text-emerald-400 hover:underline">
                start from a template
              </Link>
              .
            </li>
          )}
          {(workflows?.length ?? 0) > 0 && filtered.length === 0 && (
            <li className="px-4 py-6 text-center text-sm text-zinc-500">No workflows match “{query}”.</li>
          )}
        </ul>
      )}
    </div>
  );
}
