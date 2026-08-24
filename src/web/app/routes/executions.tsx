import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { CornerDownRight, Link2, RotateCw, X } from "lucide-react";
import { type ReactNode, useState } from "react";
import { Link } from "react-router";
import { Lamp, Rack, Silkscreen, Unit } from "../components/rack";
import { toast } from "../components/toast";
import { useConfirm } from "../components/ui/confirm";
import { ListSkeleton } from "../components/ui/skeleton";
import { api, type ExecutionSummary } from "../lib/api";
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
  const confirm = useConfirm();
  const [take, setTake] = useState(50);
  const { data: executions, isLoading } = useQuery({
    queryKey: ["executions", take],
    queryFn: () => api.executions.list(take),
  });

  // Events are workspace-scoped now, so patch the row's status in place; only fetch
  // when the execution isn't already in the list (a brand-new run).
  useEngineEvents((event) => {
    const status =
      event.type === "ExecutionStarted"
        ? "Running"
        : event.type === "ExecutionCompleted"
          ? "Succeeded"
          : event.type === "ExecutionFailed"
            ? "Failed"
            : null;

    const current = queryClient.getQueryData<ExecutionSummary[]>(["executions", take]);
    if (!current?.some((x) => x.id === event.payload.executionId)) {
      queryClient.invalidateQueries({ queryKey: ["executions"] });
      return;
    }
    if (!status) return;
    queryClient.setQueryData<ExecutionSummary[]>(["executions", take], (rows) =>
      rows?.map((x) => (x.id === event.payload.executionId ? { ...x, status } : x)),
    );
  });

  const remove = useMutation({
    mutationFn: (id: string) => api.executions.remove(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["executions"] });
      toast.success("Execution deleted.");
    },
    onError: (error) => toast.error(`Delete failed — ${String(error)}`),
  });

  const [statusFilter, setStatusFilter] = useState("All");
  const [search, setSearch] = useState("");
  const needle = search.trim().toLowerCase();
  const filteredExecutions = (executions ?? []).filter(
    (x) =>
      (statusFilter === "All" || x.status === statusFilter) && x.workflowName.toLowerCase().includes(needle),
  );

  const { roots, children } = buildTree(filteredExecutions);

  const renderRow = (execution: ExecutionSummary, depth: number, index?: number): ReactNode => (
    <Unit key={execution.id} index={index} status={execution.status}>
      <div
        className="flex min-w-0 items-center"
        style={depth > 0 ? { paddingLeft: `${depth * 1.5}rem` } : undefined}
      >
        <Link
          to={`/executions/${execution.id}`}
          className="flex min-w-0 flex-1 flex-col gap-1 py-3 pr-3 text-sm sm:flex-row sm:items-center sm:justify-between sm:gap-3"
        >
          <div className="flex min-w-0 items-center gap-3">
            {depth > 0 && (
              <span className="text-muted-foreground">
                {execution.triggeredBy.startsWith("retry:") ? (
                  <RotateCw className="h-3.5 w-3.5" aria-label="retry" />
                ) : (
                  <CornerDownRight className="h-3.5 w-3.5" aria-label="child run" />
                )}
              </span>
            )}
            <span className="min-w-0 truncate text-lg font-medium text-fg-strong">{execution.workflowName}</span>
          </div>
          <div className="flex min-w-0 items-center gap-3 sm:shrink-0">
            <Silkscreen className="shrink-0">{execution.status}</Silkscreen>
            {execution.triggeredBy === "workflow" ? (
              depth === 0 && (
                <span
                  className="inline-flex shrink-0 items-center gap-1 rounded-[2px] border border-engrave px-1.5 py-0.5 text-[11px] text-muted-foreground"
                  title="Started by another workflow (parent outside this page)"
                >
                  <Link2 className="h-3 w-3" aria-hidden="true" /> chained
                </span>
              )
            ) : execution.triggeredBy.startsWith("retry:") ? (
              depth === 0 && (
                <span
                  className="inline-flex shrink-0 items-center gap-1 rounded-[2px] border border-engrave px-1.5 py-0.5 text-[11px] text-muted-foreground"
                  title="Retry (original outside this page)"
                >
                  <RotateCw className="h-3 w-3" aria-hidden="true" /> retry
                </span>
              )
            ) : (
              <span className="shrink-0 font-mono text-[13px] text-muted-foreground">
                {execution.triggeredBy.split(":")[0]}
              </span>
            )}
            {/* Narrow widths get the clock time, which is what distinguishes one run
                from the next; the date only earns its space once there is room. */}
            <time
              dateTime={execution.startedAt}
              className="shrink-0 whitespace-nowrap font-mono text-[13px] text-muted-foreground"
            >
              <span className="sm:hidden">
                {new Date(execution.startedAt).toLocaleTimeString([], {
                  hour: "numeric",
                  minute: "2-digit",
                  second: "2-digit",
                })}
              </span>
              <span className="hidden sm:inline">
                {new Date(execution.startedAt).toLocaleString()}
              </span>
            </time>
          </div>
        </Link>
        {(execution.status === "Succeeded" || execution.status === "Failed") && (
          <button
            type="button"
            title="Delete execution"
            onClick={async () => {
              if (
                await confirm({
                  title: "Delete execution?",
                  body: "This deletes the execution and its step history.",
                  confirmLabel: "Delete",
                  destructive: true,
                })
              ) {
                remove.mutate(execution.id);
              }
            }}
            className="px-3 py-3 text-xs text-muted-foreground hover:text-danger"
            aria-label="Delete execution"
          >
            <X className="h-3.5 w-3.5" aria-hidden="true" />
          </button>
        )}
      </div>
      {(children.get(execution.id) ?? []).length > 0 && (
        <ul>{children.get(execution.id)!.map((child) => renderRow(child, depth + 1))}</ul>
      )}
    </Unit>
  );

  return (
    <div>
      <h1 className="mb-6 flex items-center gap-3 text-2xl font-semibold tracking-[-0.01em]">
        Executions
        <span className="flex items-center gap-1.5">
          <Lamp status="Running" />
          <Silkscreen>live</Silkscreen>
        </span>
      </h1>

      {isLoading && <ListSkeleton />}

      {(executions?.length ?? 0) > 0 && (
        <div className="mb-3 flex items-center gap-2">
          <input
            type="text"
            placeholder="Search by workflow…"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            className="w-full min-w-0 max-w-xs rounded-md border border-line-strong bg-card px-3 py-1.5 text-sm placeholder:text-fg-placeholder focus:border-brand-bright focus:outline-none"
          />
          <select
            value={statusFilter}
            onChange={(e) => setStatusFilter(e.target.value)}
            className="rounded-md border border-line-strong bg-card px-3 py-1.5 text-sm focus:border-brand-bright focus:outline-none"
          >
            {["All", "Running", "Succeeded", "Failed"].map((status) => (
              <option key={status} value={status}>
                {status}
              </option>
            ))}
          </select>
        </div>
      )}

      {!isLoading && (
        <Rack
          empty={
            executions?.length === 0 ? "No executions yet." : "No executions match your filter."
          }
        >
          {roots.map((execution, i) => renderRow(execution, 0, i + 1))}
        </Rack>
      )}

      {/* The list is capped server-side (newest first); reveal older runs on demand. */}
      {!isLoading && (executions?.length ?? 0) >= take && (
        <button
          type="button"
          onClick={() => setTake((current) => current + 50)}
          className="mt-3 w-full rounded-md border border-line px-4 py-2 text-sm text-muted-foreground hover:bg-card"
        >
          Load more
        </button>
      )}
    </div>
  );
}
