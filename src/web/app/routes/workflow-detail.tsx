import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { Link, useNavigate, useParams } from "react-router";
import { api } from "../lib/api";
import { DriftWarning, SourceBadge } from "../components/action-source";
import { CodeBlock } from "../components/code-block";
import { toast } from "../components/toast";

const inputClass =
  "rounded-md border border-zinc-700 bg-zinc-900 px-3 py-1.5 text-sm " +
  "placeholder:text-zinc-600 focus:border-emerald-500 focus:outline-none";

export default function WorkflowDetail() {
  const { id = "" } = useParams();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [triggerType, setTriggerType] = useState("cron");
  const [cron, setCron] = useState("*/5 * * * *");
  const [chainWorkflowId, setChainWorkflowId] = useState("");
  const [chainOn, setChainOn] = useState("succeeded");
  const [payload, setPayload] = useState("");
  const [newWebhook, setNewWebhook] = useState<string | null>(null);

  const { data: workflow, isLoading, error } = useQuery({
    queryKey: ["workflow", id],
    queryFn: () => api.workflows.get(id),
    retry: false,
  });

  const { data: allWorkflows } = useQuery({
    queryKey: ["workflows"],
    queryFn: api.workflows.list,
    staleTime: 60_000,
  });

  const execute = useMutation({
    mutationFn: () => api.workflows.execute(id, payload.trim() || undefined),
    onSuccess: ({ executionId }) => navigate(`/executions/${executionId}`),
  });

  const addTrigger = useMutation({
    mutationFn: () =>
      api.triggers.create(id, {
        type: triggerType,
        config:
          triggerType === "cron"
            ? { cron }
            : triggerType === "workflow"
              ? { workflowId: chainWorkflowId, on: chainOn }
              : {},
      }),
    onSuccess: (created) => {
      setNewWebhook(created.webhookUrl);
      queryClient.invalidateQueries({ queryKey: ["workflow", id] });
    },
  });

  const removeTrigger = useMutation({
    mutationFn: (triggerId: string) => api.triggers.remove(triggerId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["workflow", id] });
      toast.success("Trigger deleted.");
    },
    onError: (error) => toast.error(`Trigger delete failed — ${String(error)}`),
  });

  const rotateSecret = useMutation({
    mutationFn: (triggerId: string) => api.triggers.rotateSecret(triggerId),
    onSuccess: (rotated) => setNewWebhook(rotated.webhookUrl),
  });

  const removeWorkflow = useMutation({
    mutationFn: () => api.workflows.remove(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["workflows"] });
      toast.success("Workflow and its execution history deleted.");
      navigate("/");
    },
    onError: (error) => toast.error(`Delete failed — ${String(error)}`),
  });

  const restoreVersion = useMutation({
    mutationFn: (version: number) => api.workflows.restoreVersion(id, version),
    onSuccess: (result, version) => {
      queryClient.invalidateQueries({ queryKey: ["workflow", id] });
      toast.success(`Restored v${version} as v${result.version}.`);
    },
    onError: (error) => toast.error(`Restore failed — ${String(error)}`),
  });

  if (isLoading) return <p className="text-sm text-zinc-500">Loading…</p>;
  if (error || !workflow) {
    return (
      <p className="text-sm text-zinc-500">
        Workflow not found in this workspace —{" "}
        <a href="/" className="text-emerald-400 hover:underline">
          back to workflows
        </a>
        .
      </p>
    );
  }

  return (
    <div className="space-y-8">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-lg font-semibold">{workflow.name}</h1>
          {workflow.description && <p className="text-sm text-zinc-500">{workflow.description}</p>}
        </div>
        <div className="flex items-center gap-3">
          <button
            type="button"
            onClick={() => {
              if (window.confirm(`Delete "${workflow.name}" and its entire execution history?`)) {
                removeWorkflow.mutate();
              }
            }}
            disabled={removeWorkflow.isPending}
            className="text-sm text-zinc-500 hover:text-red-400 disabled:opacity-50"
          >
            Delete
          </button>
          <button
            type="button"
            onClick={async () => {
              try {
                const doc = await api.workflows.export(id);
                const blob = new Blob([JSON.stringify(doc, null, 2)], { type: "application/json" });
                const anchor = document.createElement("a");
                anchor.href = URL.createObjectURL(blob);
                anchor.download = `${workflow.name}.workflow.json`;
                anchor.click();
                URL.revokeObjectURL(anchor.href);
              } catch (exportError) {
                toast.error(`Export failed — ${String(exportError)}`);
              }
            }}
            className="text-sm text-zinc-500 hover:text-zinc-200"
          >
            Export
          </button>
          <Link
            to={`/workflows/${id}/edit`}
            className="rounded-md border border-zinc-700 px-3 py-1.5 text-sm hover:bg-zinc-900"
          >
            Edit
          </Link>
          <button
            type="button"
            onClick={() => execute.mutate()}
            disabled={execute.isPending}
            className="rounded-md bg-emerald-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-emerald-500 disabled:opacity-50"
          >
            {execute.isPending ? "Starting…" : "Run now"}
          </button>
        </div>
      </div>
      {removeWorkflow.error && <p className="text-sm text-red-400">{String(removeWorkflow.error)}</p>}

      <div>
        <textarea
          className="w-full rounded-md border border-zinc-800 bg-zinc-900 px-3 py-1.5 font-mono text-xs placeholder:text-zinc-600 focus:border-emerald-500 focus:outline-none"
          rows={2}
          placeholder='Optional JSON payload for Run now — available in steps as {{trigger.payload}}'
          value={payload}
          onChange={(e) => setPayload(e.target.value)}
        />
        {execute.error && <p className="mt-1 text-sm text-red-400">{String(execute.error)}</p>}
      </div>

      {(workflow.runsAfter.length > 0 || workflow.feeds.length > 0) && (
        <div className="space-y-1 rounded-md border border-violet-500/30 bg-violet-500/5 px-3 py-2 text-sm">
          {workflow.runsAfter.map((link) => (
            <p key={`after-${link.workflowId}-${link.on}`}>
              ⛓ Runs after{" "}
              <Link to={`/workflows/${link.workflowId}`} className="text-violet-400 hover:underline">
                {link.name}
              </Link>{" "}
              <span className="text-xs text-zinc-500">({onLabel(link.on)})</span>
            </p>
          ))}
          {workflow.feeds.map((link) => (
            <p key={`feeds-${link.workflowId}-${link.on}`}>
              ⛓ Feeds{" "}
              <Link to={`/workflows/${link.workflowId}`} className="text-violet-400 hover:underline">
                {link.name}
              </Link>{" "}
              <span className="text-xs text-zinc-500">({onLabel(link.on)})</span>
            </p>
          ))}
        </div>
      )}

      <section>
        <h2 className="mb-3 text-sm font-medium text-zinc-300">
          Steps <span className="text-zinc-500">(v{workflow.latestVersion.version})</span>
        </h2>
        <ol className="space-y-2">
          {workflow.latestVersion.steps.map((step) => (
            <li key={step.id} className="rounded-lg border border-zinc-800 px-4 py-3">
              <div className="flex items-center gap-3">
                <span className="text-xs text-zinc-500">#{step.order + 1}</span>
                <span className="text-sm font-medium">{step.name ?? step.actionType}</span>
                <span className="rounded bg-zinc-800 px-1.5 py-0.5 text-xs text-zinc-400">
                  {step.actionType}
                </span>
                <SourceBadge actionType={step.actionType} />
              </div>
              <CodeBlock text={JSON.stringify(JSON.parse(step.configJson), null, 2)} />
              <DriftWarning actionType={step.actionType} configJson={step.configJson} />
            </li>
          ))}
        </ol>
      </section>

      {workflow.versions.length > 1 && (
        <section>
          <h2 className="mb-3 text-sm font-medium text-zinc-300">Versions</h2>
          <ul className="space-y-1.5">
            {workflow.versions.map((version) => (
              <li
                key={version.id}
                className="flex items-center justify-between rounded-lg border border-zinc-800 px-4 py-2 text-sm"
              >
                <div className="flex items-center gap-3">
                  <span className="font-medium">v{version.version}</span>
                  {version.version === workflow.latestVersion.version && (
                    <span className="text-xs text-emerald-400">current</span>
                  )}
                  <span className="text-xs text-zinc-500">
                    {version.stepCount} step{version.stepCount === 1 ? "" : "s"} ·{" "}
                    {new Date(version.createdAt).toLocaleString()}
                  </span>
                </div>
                {version.version !== workflow.latestVersion.version && (
                  <button
                    type="button"
                    disabled={restoreVersion.isPending}
                    onClick={() => {
                      if (
                        window.confirm(
                          `Restore v${version.version}? Its steps become v${workflow.latestVersion.version + 1} — nothing is rewritten.`,
                        )
                      ) {
                        restoreVersion.mutate(version.version);
                      }
                    }}
                    className="text-xs text-zinc-500 hover:text-emerald-400 disabled:opacity-50"
                  >
                    Restore
                  </button>
                )}
              </li>
            ))}
          </ul>
          {restoreVersion.error && (
            <p className="mt-2 text-sm text-red-400">{String(restoreVersion.error)}</p>
          )}
        </section>
      )}

      <section>
        <h2 className="mb-3 text-sm font-medium text-zinc-300">Triggers</h2>
        <ul className="mb-4 space-y-2">
          {workflow.triggers.map((trigger) => (
            <li
              key={trigger.id}
              className="flex items-center justify-between rounded-lg border border-zinc-800 px-4 py-2.5 text-sm"
            >
              <div className="flex items-center gap-3">
                <span className="font-medium">{trigger.type}</span>
                {!trigger.enabled && <span className="text-xs text-red-400">disabled</span>}
                {trigger.type === "webhook" && (
                  <code className="text-xs text-zinc-500">POST /api/webhooks/{trigger.id}?secret=•••</code>
                )}
                {trigger.type === "workflow" && (
                  <ChainSummary configJson={trigger.configJson} workflows={allWorkflows} />
                )}
                {trigger.nextRunAt && (
                  <span className="text-xs text-zinc-500">
                    next: {new Date(trigger.nextRunAt).toLocaleString()}
                  </span>
                )}
              </div>
              <div className="flex gap-3">
                {trigger.type === "webhook" && (
                  <button
                    type="button"
                    onClick={() => rotateSecret.mutate(trigger.id)}
                    className="text-xs text-zinc-500 hover:text-amber-400"
                  >
                    Rotate secret
                  </button>
                )}
                <button
                  type="button"
                  onClick={() => removeTrigger.mutate(trigger.id)}
                  className="text-xs text-zinc-500 hover:text-red-400"
                >
                  Delete
                </button>
              </div>
            </li>
          ))}
          {workflow.triggers.length === 0 && (
            <li className="text-sm text-zinc-500">No triggers — runs manually only.</li>
          )}
        </ul>

        <div className="flex items-center gap-2">
          <select
            className={inputClass}
            value={triggerType}
            onChange={(e) => setTriggerType(e.target.value)}
          >
            <option value="cron">cron</option>
            <option value="webhook">webhook</option>
            <option value="workflow">workflow (chain)</option>
          </select>
          {triggerType === "workflow" && (
            <>
              <span className="text-xs text-zinc-500">when</span>
              <select
                className={inputClass}
                value={chainWorkflowId}
                onChange={(e) => setChainWorkflowId(e.target.value)}
              >
                <option value="">choose workflow…</option>
                {allWorkflows?.map((candidate) => (
                  <option key={candidate.id} value={candidate.id}>
                    {candidate.name}
                  </option>
                ))}
              </select>
              <select className={inputClass} value={chainOn} onChange={(e) => setChainOn(e.target.value)}>
                <option value="succeeded">succeeds</option>
                <option value="failed">fails</option>
                <option value="any">finishes (any)</option>
              </select>
            </>
          )}
          {triggerType === "cron" && (
            <input
              className={`${inputClass} font-mono`}
              value={cron}
              onChange={(e) => setCron(e.target.value)}
              placeholder="*/5 * * * *"
            />
          )}
          <button
            type="button"
            onClick={() => addTrigger.mutate()}
            disabled={addTrigger.isPending || (triggerType === "workflow" && !chainWorkflowId)}
            className="rounded-md border border-zinc-700 px-3 py-1.5 text-sm hover:bg-zinc-900 disabled:opacity-50"
          >
            Add trigger
          </button>
        </div>
        {addTrigger.error && <p className="mt-2 text-sm text-red-400">{String(addTrigger.error)}</p>}
        {newWebhook && (
          <div className="mt-3 rounded-md border border-amber-500/40 bg-amber-500/10 px-3 py-2 text-sm">
            <span className="font-medium text-amber-400">Copy now — shown once: </span>
            <code className="break-all text-xs">
              POST {newWebhook.startsWith("http") ? newWebhook : `${window.location.origin}${newWebhook}`}
            </code>
          </div>
        )}
      </section>
    </div>
  );
}

function ChainSummary({
  configJson,
  workflows,
}: {
  configJson: string;
  workflows: { id: string; name: string }[] | undefined;
}) {
  try {
    const config = JSON.parse(configJson) as { workflowId?: string; on?: string };
    const name = workflows?.find((w) => w.id === config.workflowId)?.name ?? config.workflowId;
    const on = config.on === "failed" ? "fails" : config.on === "any" ? "finishes" : "succeeds";
    return (
      <span className="text-xs text-zinc-500">
        ⛓ when{" "}
        <Link to={`/workflows/${config.workflowId}`} className="text-violet-400 hover:underline">
          {name}
        </Link>{" "}
        {on}
      </span>
    );
  } catch {
    return null;
  }
}

const onLabel = (on: string) =>
  on === "failed" ? "on failure" : on === "any" ? "on any finish" : "on success";
