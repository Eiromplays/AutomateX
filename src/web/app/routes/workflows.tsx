import { useQuery } from "@tanstack/react-query";
import { Link2, Pause } from "lucide-react";
import { useState } from "react";
import { Link, useNavigate } from "react-router";
import { Rack, Unit } from "../components/rack";
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
        <h1 className="text-2xl font-semibold tracking-[-0.01em]">Workflows</h1>
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
            className="rounded-[2px] bg-primary px-3 py-1.5 text-sm font-medium text-primary-foreground hover:bg-brand-bright"
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
          className="mb-3 w-full max-w-sm rounded-[2px] border border-line-strong bg-card px-3 py-1.5 text-sm placeholder:text-fg-placeholder focus:border-brand-bright focus:outline-none"
        />
      )}

      {!isLoading && (
        <Rack
          empty={
            workflows?.length === 0 ? (
              <>
                No workflows yet — create the first one, or{" "}
                <Link to="/templates" className="text-fg-body underline underline-offset-4">
                  start from a template
                </Link>
                .
              </>
            ) : (
              `No workflows match “${query}”.`
            )
          }
        >
          {filtered.map((workflow, i) => (
            <Unit
              key={workflow.id}
              index={i + 1}
              status={workflow.enabled ? "running" : "disabled"}
              align="start"
            >
              <Link
                to={`/workflows/${workflow.id}`}
                className="flex flex-col gap-1 py-3 pr-3 sm:flex-row sm:items-start sm:justify-between sm:gap-3"
              >
                <div className="min-w-0">
                  <div className="truncate text-lg font-medium text-fg-strong">{workflow.name}</div>
                  {workflow.description && (
                    <div className="max-w-[38em] text-sm text-muted-foreground">{workflow.description}</div>
                  )}
                  {workflow.runsAfter.length > 0 && (
                    <div className="flex items-center gap-1.5 text-sm text-muted-foreground">
                      <Link2 className="h-3.5 w-3.5 shrink-0" aria-hidden="true" />
                      runs after {workflow.runsAfter.join(", ")}
                    </div>
                  )}
                  {workflow.feeds.length > 0 && (
                    <div className="flex items-center gap-1.5 text-sm text-muted-foreground">
                      <Link2 className="h-3.5 w-3.5 shrink-0" aria-hidden="true" />
                      feeds {workflow.feeds.join(", ")}
                    </div>
                  )}
                </div>
                <span className="flex shrink-0 items-center gap-3 font-mono text-[13px] text-muted-foreground">
                  {!workflow.enabled && (
                    <span className="inline-flex items-center gap-1.5">
                      <Pause className="h-3 w-3" aria-hidden="true" />
                      paused
                    </span>
                  )}
                  v{workflow.latestVersion}
                </span>
              </Link>
            </Unit>
          ))}

        </Rack>
      )}
    </div>
  );
}
