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
      <div className="mb-6 flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
        <h1 className="text-lg font-semibold">Workflows</h1>
        <div className="flex items-center gap-3">
          <label className="cursor-pointer text-sm text-muted-foreground hover:text-fg-emphasis">
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
          <Link to="/templates" className="text-sm text-muted-foreground hover:text-fg-strong">
            Templates
          </Link>
          <Link
            to="/workflows/new"
            className="rounded-md bg-primary px-3 py-1.5 text-sm font-medium text-primary-foreground hover:bg-brand-bright"
          >
            New workflow
          </Link>
        </div>
      </div>

      {isLoading && <ListSkeleton />}
      {error && <p className="text-sm text-danger">{String(error)}</p>}

      {(workflows?.length ?? 0) > 0 && (
        <input
          type="text"
          placeholder="Search workflows…"
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          className="mb-3 w-full max-w-sm rounded-md border border-line-strong bg-card px-3 py-1.5 text-sm placeholder:text-fg-placeholder focus:border-brand-bright focus:outline-none"
        />
      )}

      {!isLoading && (
        <ul className="divide-y divide-zinc-800 rounded-lg border border-line">
          {filtered.map((workflow) => (
            <li key={workflow.id}>
              <Link
                to={`/workflows/${workflow.id}`}
                className="flex items-center justify-between gap-3 px-4 py-3 hover:bg-card"
              >
                <div className="min-w-0">
                  <div className="truncate text-sm font-medium">{workflow.name}</div>
                  {workflow.description && (
                    <div className="text-xs text-muted-foreground">{workflow.description}</div>
                  )}
                  {workflow.runsAfter.length > 0 && (
                    <div className="text-xs text-trigger">
                      ⛓ runs after {workflow.runsAfter.join(", ")}
                    </div>
                  )}
                  {workflow.feeds.length > 0 && (
                    <div className="text-xs text-trigger">⛓ feeds {workflow.feeds.join(", ")}</div>
                  )}
                </div>
                <span className="flex shrink-0 items-center gap-2 text-xs text-muted-foreground">
                  {!workflow.enabled && <span className="text-warning">⏸ paused</span>}v
                  {workflow.latestVersion}
                </span>
              </Link>
            </li>
          ))}
          {workflows?.length === 0 && (
            <li className="px-4 py-6 text-center text-sm text-muted-foreground">
              No workflows yet — create the first one, or{" "}
              <Link to="/templates" className="text-brand hover:underline">
                start from a template
              </Link>
              .
            </li>
          )}
          {(workflows?.length ?? 0) > 0 && filtered.length === 0 && (
            <li className="px-4 py-6 text-center text-sm text-muted-foreground">No workflows match “{query}”.</li>
          )}
        </ul>
      )}
    </div>
  );
}
