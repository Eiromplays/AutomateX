import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { Link, useNavigate, useParams } from "react-router";
import { SourceBadge } from "../components/action-source";
import { CodeBlock } from "../components/code-block";
import { StatusBadge } from "../components/status-badge";
import { backboneEdges } from "../components/switch-routing";
import { toast } from "../components/toast";
import { useConfirm } from "../components/ui/confirm";
import { Dialog, DialogContent } from "../components/ui/dialog";
import { type GraphTrigger, WorkflowGraph } from "../components/workflow-graph";
import { triggerSummary } from "../components/workflow-triggers";
import {
  api,
  type ExecutionDetail as ExecutionDetailData,
  type ExecutionStep,
  type WorkflowTrigger,
} from "../lib/api";
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

// triggeredBy is "<type>:<triggerId>" for cron/webhook/rss/http.poll, or a bare word for
// manual/scheduled/workflow, or "retry:<executionId>".
function parseTriggeredBy(triggeredBy: string): { type: string; id?: string } {
  const i = triggeredBy.indexOf(":");
  return i === -1 ? { type: triggeredBy } : { type: triggeredBy.slice(0, i), id: triggeredBy.slice(i + 1) };
}

function triggerLabel(t: WorkflowTrigger): string {
  let config: Record<string, unknown> = {};
  try {
    config = JSON.parse(t.configJson) as Record<string, unknown>;
  } catch {
    config = {};
  }
  return triggerSummary({ key: 0, type: t.type, config, enabled: t.enabled });
}

// The specific trigger that started this run, if it still exists on the workflow.
function firingTrigger(triggeredBy: string, triggers: WorkflowTrigger[]): WorkflowTrigger | undefined {
  const { id } = parseTriggeredBy(triggeredBy);
  return id ? triggers.find((t) => t.id === id) : undefined;
}

// Human label for the "Triggered by" field — disambiguates between same-type triggers by config.
function describeTriggeredBy(triggeredBy: string, triggers: WorkflowTrigger[]): string {
  if (triggeredBy === "manual") return "Manual run";
  if (triggeredBy === "scheduled") return "Scheduled action";
  if (triggeredBy === "workflow") return "Workflow chain";
  if (triggeredBy.startsWith("retry:")) return "Retry";
  const { type } = parseTriggeredBy(triggeredBy);
  const t = firingTrigger(triggeredBy, triggers);
  // triggerLabel already embeds the type (e.g. "http.poll · <url>"), so don't prefix it again.
  return t ? triggerLabel(t) : type;
}

// The single trigger node to draw in the run graph — the trigger that fired, edged into the step it
// started the run at (its entry step, or the first step). Falls back to a generic label for
// manual/scheduled/chained/retry runs that have no trigger row.
function triggerNodes(execution: ExecutionDetailData, triggers: WorkflowTrigger[]): GraphTrigger[] {
  if (execution.workflowSteps.length === 0) return [];
  const firstOrder = [...execution.workflowSteps].sort((a, b) => a.order - b.order)[0].order;
  const firing = firingTrigger(execution.triggeredBy, triggers);
  return [
    {
      key: 0,
      label: firing ? triggerLabel(firing) : describeTriggeredBy(execution.triggeredBy, triggers),
      entryStepKey: firing?.entryStepOrder ?? firstOrder,
    },
  ];
}

const STEP_COLOR: Record<string, string> = {
  Succeeded: "bg-green-500/70",
  Failed: "bg-red-500/70",
  // Caught: failed but handled by an error edge — amber, distinct from a hard failure.
  Caught: "bg-orange-500/70",
  Waiting: "bg-sky-500/70",
  Skipped: "bg-zinc-600/60",
  Running: "bg-amber-500/70",
  Pending: "bg-zinc-700/50",
};

// The gate step records {open, reason} as its output — surface it as a verdict.
function gateInfo(step: ExecutionStep): { open: boolean; reason: string } | null {
  if (step.actionType !== "gate" || !step.output) return null;
  try {
    const parsed = JSON.parse(step.output) as {
      open?: boolean;
      reason?: string;
    };
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
  const confirm = useConfirm();
  const [selectedStepOrder, setSelectedStepOrder] = useState<number | null>(null);

  const retry = useMutation({
    mutationFn: () => api.executions.retry(id),
    onSuccess: ({ executionId }) => {
      toast.success("Retried with the original payload.");
      navigate(`/executions/${executionId}`);
    },
    onError: (retryError) => toast.error(`Retry failed — ${String(retryError)}`),
  });

  const resume = useMutation({
    mutationFn: () => api.executions.resume(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["execution", id] });
      toast.success("Resumed.");
    },
    onError: (resumeError) => toast.error(`Resume failed — ${String(resumeError)}`),
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

  const {
    data: execution,
    isLoading,
    error,
  } = useQuery({
    queryKey: ["execution", id],
    queryFn: () => api.executions.get(id),
    retry: false,
  });

  // The workflow's current triggers — to name which trigger fired and draw it in the graph.
  const { data: workflow } = useQuery({
    queryKey: ["workflow", execution?.workflowId],
    queryFn: () => api.workflows.get(execution!.workflowId),
    enabled: !!execution,
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
          {execution.workflowVersion != null && (
            <span className="text-sm text-zinc-500">v{execution.workflowVersion}</span>
          )}
          <StatusBadge status={execution.status} />
          <span className="text-xs font-normal text-emerald-400">● live</span>
        </div>
        <div className="flex items-center gap-4">
          {execution.status === "Waiting" && (
            <button
              type="button"
              onClick={() => resume.mutate()}
              disabled={resume.isPending}
              title="Resume this paused run (the wait step continues)"
              className="text-sm text-sky-400 hover:text-sky-300 disabled:opacity-50"
            >
              {resume.isPending ? "Resuming…" : "▶ Resume"}
            </button>
          )}
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
              onClick={async () => {
                if (
                  await confirm({
                    title: "Delete execution?",
                    body: "This deletes the execution and its step history.",
                    confirmLabel: "Delete",
                    destructive: true,
                  })
                ) {
                  remove.mutate();
                }
              }}
              disabled={remove.isPending}
              className="text-sm text-zinc-500 hover:text-red-400 disabled:opacity-50"
            >
              Delete
            </button>
          )}
          <Link
            to={`/workflows/${execution.workflowId}`}
            className="text-sm text-zinc-400 hover:text-zinc-100"
          >
            View workflow →
          </Link>
        </div>
      </div>
      {remove.error && <p className="text-sm text-red-400">{String(remove.error)}</p>}

      <ChainLineage execution={execution} />

      <dl className="grid grid-cols-2 gap-3 text-sm sm:grid-cols-4">
        <div>
          <dt className="text-xs text-zinc-500">Triggered by</dt>
          <dd>{describeTriggeredBy(execution.triggeredBy, workflow?.triggers ?? [])}</dd>
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

      <ExecutionGraph
        execution={execution}
        triggers={triggerNodes(execution, workflow?.triggers ?? [])}
        selection={selectedStepOrder}
        onSelect={setSelectedStepOrder}
      />

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

      <p className="text-xs text-zinc-600">
        Click a step in the graph to inspect its output, errors and timing.
      </p>

      <StepDialog
        execution={execution}
        order={selectedStepOrder}
        onClose={() => setSelectedStepOrder(null)}
      />
    </div>
  );
}

// Per-step inspector, opened by clicking a graph node. A node with no run row (never reached)
// shows that instead of details.
function StepDialog({
  execution,
  order,
  onClose,
}: {
  execution: ExecutionDetailData;
  order: number | null;
  onClose: () => void;
}) {
  const step = order != null ? (execution.steps.find((s) => s.stepOrder === order) ?? null) : null;
  const versionStep = order != null ? (execution.workflowSteps.find((s) => s.order === order) ?? null) : null;
  const title = versionStep
    ? `#${versionStep.order + 1} ${versionStep.name ?? versionStep.actionType}`
    : "Step";
  const gate = step ? gateInfo(step) : null;

  return (
    <Dialog
      open={order != null}
      onOpenChange={(open) => {
        if (!open) onClose();
      }}
    >
      <DialogContent title={title}>
        {!step ? (
          <p className="text-sm text-zinc-500">This step did not run in this execution.</p>
        ) : (
          <div className="space-y-3">
            <div className="flex flex-wrap items-center gap-3">
              <span className="rounded bg-zinc-800 px-1.5 py-0.5 text-xs text-zinc-400">
                {step.actionType}
              </span>
              <SourceBadge actionType={step.actionType} />
              <StatusBadge status={step.status} />
              <span className="text-xs text-zinc-500">
                {step.attempts} attempt{step.attempts === 1 ? "" : "s"}
                {step.failedAttempts > 0 && ` (${step.failedAttempts} failed)`}
              </span>
              <span className="text-xs text-zinc-500">{fmtMs(diffMs(step.startedAt, step.completedAt))}</span>
            </div>
            {gate ? (
              <div
                className={`inline-flex items-center gap-1.5 rounded px-2 py-0.5 text-xs ${
                  gate.open ? "bg-green-500/15 text-green-400" : "bg-amber-500/15 text-amber-400"
                }`}
              >
                {gate.open ? "⛩ Gate open — workflow continued" : `⛩ Gate closed — ${gate.reason}`}
              </div>
            ) : (
              step.output && <CodeBlock text={prettyJson(step.output)} />
            )}
            {step.error && <CodeBlock text={step.error} tone="error" />}
          </div>
        )}
      </DialogContent>
    </Dialog>
  );
}

// The ran version's DAG, each node tinted by how that step actually went (succeeded / failed /
// skipped / running, or untinted when it never ran). Linear runs fall back to the order backbone.
function ExecutionGraph({
  execution,
  triggers,
  selection,
  onSelect,
}: {
  execution: ExecutionDetailData;
  triggers: GraphTrigger[];
  selection: number | null;
  onSelect: (order: number | null) => void;
}) {
  if (execution.workflowSteps.length === 0) return null;

  const statusByOrder = new Map(execution.steps.map((s) => [s.stepOrder, s.status]));
  const graphSteps = execution.workflowSteps.map((s) => ({
    key: s.order,
    label: s.name ?? s.actionType,
    actionType: s.actionType,
    status: statusByOrder.get(s.order),
  }));
  const stepEdges =
    execution.edges.length > 0
      ? execution.edges.map((e) => ({
          sourceKey: e.from,
          targetKey: e.to,
          label: e.label,
        }))
      : backboneEdges(execution.workflowSteps.map((s) => s.order));

  return (
    <div className="space-y-1">
      <div className="text-xs text-zinc-500">Run graph</div>
      <WorkflowGraph
        steps={graphSteps}
        triggers={triggers}
        stepEdges={stepEdges}
        selection={selection}
        onSelect={(sel) => onSelect(typeof sel === "number" ? sel : null)}
        height="18rem"
      />
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

  if (!lineage && !retryOf && execution.chained.length === 0 && execution.retries.length === 0) return null;

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
      {execution.retries.map((r) => (
        <p key={r.executionId}>
          ↻ Retried as execution{" "}
          <Link to={`/executions/${r.executionId}`} className="text-violet-400 hover:underline">
            {r.executionId.slice(0, 13)}…
          </Link>{" "}
          <span className="text-xs text-zinc-500">({r.status})</span>
        </p>
      ))}
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
