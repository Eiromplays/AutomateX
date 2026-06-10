import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Link, useNavigate, useParams } from "react-router";
import { api, type ExecutionDetail as ExecutionDetailData, type ExecutionStep } from "../lib/api";
import { SourceBadge } from "../components/action-source";
import { StatusBadge } from "../components/status-badge";
import { CodeBlock } from "../components/code-block";
import { toast } from "../components/toast";
import { useEngineEvents } from "../lib/use-engine-events";

function diffMs(start: string, end: string | null): number | null {
  if (!end) return null;
  return new Date(end).getTime() - new Date(start).getTime();
}

function fmtMs(ms: number | null): string {
  if (ms === null) return "—";
  return ms < 1000 ? `${ms}ms` : `${(ms / 1000).toFixed(ms >= 10000 ? 0 : 1)}s`;
}

function prettyJson(text: string): string {
  try {
    return JSON.stringify(JSON.parse(text), null, 2);
  } catch {
    return text;
  }
}

const STEP_COLOR: Record<string, string> = {
  Succeeded: "bg-green-500/70",
  Failed: "bg-red-500/70",
  Skipped: "bg-zinc-600/60",
  Running: "bg-amber-500/70",
  Pending: "bg-zinc-700/50",
};

// The gate step records {open, reason} as its output — surface it as a verdict.
function gateInfo(step: ExecutionStep): { open: boolean; reason: string } | null {
  if (step.actionType !== "gate" || !step.output) return null;
  try {
    const parsed = JSON.parse(step.output) as { open?: boolean; reason?: string };
    return typeof parsed.open === "boolean" ? { open: parsed.open, reason: parsed.reason ?? "" } : null;
  } catch {
    return null;
  }
}

function Timeline({ execution }: { execution: ExecutionDetailData }) {
  const start = new Date(execution.startedAt).getTime();
  const end = execution.completedAt ? new Date(execution.completedAt).getTime() : Date.now();
  const total = Math.max(1, end - start);

  return (
    <div className="relative h-6 w-full overflow-hidden rounded border border-zinc-800 bg-zinc-900">
      {execution.steps.map((s) => {
        const segStart = new Date(s.startedAt).getTime();
        const segEnd = s.completedAt ? new Date(s.completedAt).getTime() : end;
        const left = Math.min(((segStart - start) / total) * 100, 99);
        const width = Math.max(0.8, ((segEnd - segStart) / total) * 100);
        return (
          <div
            key={s.id}
            title={`#${s.stepOrder + 1} ${s.actionType} · ${fmtMs(diffMs(s.startedAt, s.completedAt))}`}
            className={`absolute top-0 h-full ${STEP_COLOR[s.status] ?? "bg-zinc-600/60"}`}
            style={{ left: `${left}%`, width: `${width}%` }}
          />
        );
      })}
    </div>
  );
}

export default function ExecutionDetail() {
  const { id = "" } = useParams();
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const retry = useMutation({
    mutationFn: () => api.executions.retry(id),
    onSuccess: ({ executionId }) => {
      toast.success("Retried with the original payload.");
      navigate(`/executions/${executionId}`);
    },
    onError: (retryError) => toast.error(`Retry failed — ${String(retryError)}`),
  });

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
              onClick={() => retry.mutate()}
              disabled={retry.isPending}
              title="Re-run on the latest version with the original trigger payload"
              className="text-sm text-zinc-400 hover:text-emerald-400 disabled:opacity-50"
            >
              {retry.isPending ? "Retrying…" : execution.status === "Failed" ? "↻ Retry" : "↻ Run again"}
            </button>
          )}
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
          <dd>{fmtMs(diffMs(execution.startedAt, execution.completedAt))}</dd>
        </div>
      </dl>

      {execution.steps.length > 0 && (
        <div className="space-y-1">
          <div className="text-xs text-zinc-500">Timeline</div>
          <Timeline execution={execution} />
        </div>
      )}

      {execution.triggerPayload && (
        <details className="rounded-lg border border-zinc-800">
          <summary className="cursor-pointer px-4 py-2 text-sm text-zinc-400 hover:text-zinc-200">
            Trigger payload <span className="text-xs text-zinc-600">· the data this run started with</span>
          </summary>
          <div className="px-4 pb-3">
            <CodeBlock text={prettyJson(execution.triggerPayload)} />
          </div>
        </details>
      )}

      <ol className="space-y-3">
        {execution.steps.map((step) => {
          const gate = gateInfo(step);
          return (
            <li key={step.id} className="rounded-lg border border-zinc-800 p-4">
              <div className="flex items-center gap-3">
                <span className="text-xs text-zinc-500">#{step.stepOrder + 1}</span>
                <span className="rounded bg-zinc-800 px-1.5 py-0.5 text-xs text-zinc-400">{step.actionType}</span>
                <SourceBadge actionType={step.actionType} />
                <StatusBadge status={step.status} />
                <span className="text-xs text-zinc-500">
                  {step.attempts} attempt{step.attempts === 1 ? "" : "s"}
                  {step.failedAttempts > 0 && ` (${step.failedAttempts} failed)`}
                </span>
                <span className="ml-auto text-xs text-zinc-500">{fmtMs(diffMs(step.startedAt, step.completedAt))}</span>
              </div>
              {gate ? (
                <div
                  className={`mt-2 inline-flex items-center gap-1.5 rounded px-2 py-0.5 text-xs ${
                    gate.open ? "bg-green-500/15 text-green-400" : "bg-amber-500/15 text-amber-400"
                  }`}
                >
                  {gate.open ? "⛩ Gate open — workflow continued" : `⛩ Gate closed — ${gate.reason}`}
                </div>
              ) : (
                step.output && <CodeBlock text={prettyJson(step.output)} />
              )}
              {step.error && <CodeBlock text={step.error} tone="error" />}
            </li>
          );
        })}
        {execution.steps.length === 0 && <li className="text-sm text-zinc-500">No steps recorded.</li>}
      </ol>
    </div>
  );
}

// Upstream lineage: parsed from the trigger payload the chained execution carries.
function ChainLineage({ execution }: { execution: ExecutionDetailData }) {
  const retryOf = execution.triggeredBy.startsWith("retry:") ? execution.triggeredBy.slice(6) : null;

  const lineage = (() => {
    if (execution.triggeredBy !== "workflow" || !execution.triggerPayload) return null;
    try {
      const payload = JSON.parse(execution.triggerPayload) as {
        chainDepth?: number;
        source?: { executionId?: string; status?: string };
      };
      return payload.source?.executionId ? { ...payload.source, depth: payload.chainDepth ?? 1 } : null;
    } catch {
      return null;
    }
  })();

  if (!lineage && !retryOf && execution.chained.length === 0) return null;

  return (
    <div className="space-y-1 rounded-md border border-violet-500/30 bg-violet-500/5 px-3 py-2 text-sm">
      {retryOf && (
        <p>
          ↻ Retry of execution{" "}
          <Link to={`/executions/${retryOf}`} className="text-violet-400 hover:underline">
            {retryOf.slice(0, 13)}…
          </Link>{" "}
          <span className="text-xs text-zinc-500">(same payload, latest version)</span>
        </p>
      )}
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
