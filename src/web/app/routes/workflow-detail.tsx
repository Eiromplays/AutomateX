import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { Link, useNavigate, useParams } from "react-router";
import { api, type WorkflowTrigger } from "../lib/api";
import { toast } from "../components/toast";
import { CodeBlock } from "../components/code-block";
import { WorkflowGraph } from "../components/workflow-graph";
import { backboneEdges } from "../components/switch-routing";
import { triggerSummary } from "../components/workflow-triggers";
import { Dialog, DialogContent } from "../components/ui/dialog";
import { useConfirm } from "../components/ui/confirm";
import {
  DropdownMenu,
  DropdownMenuTrigger,
  DropdownMenuContent,
  DropdownMenuItem,
} from "../components/ui/dropdown-menu";

function triggerNodeLabel(trigger: WorkflowTrigger): string {
  let config: Record<string, unknown> = {};
  try {
    config = JSON.parse(trigger.configJson) as Record<string, unknown>;
  } catch {
    config = {};
  }
  return triggerSummary({ key: 0, type: trigger.type, config, enabled: trigger.enabled });
}

function prettyJson(json: string): string {
  try {
    return JSON.stringify(JSON.parse(json), null, 2);
  } catch {
    return json;
  }
}

export default function WorkflowDetail() {
  const { id = "" } = useParams();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const confirm = useConfirm();
  const [payload, setPayload] = useState("");
  const [newWebhook, setNewWebhook] = useState<string | null>(null);
  const [selectedStepOrder, setSelectedStepOrder] = useState<number | null>(null);
  const [selectedTriggerIndex, setSelectedTriggerIndex] = useState<number | null>(null);
  const [runOpen, setRunOpen] = useState(false);

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

  const updateTrigger = useMutation({
    mutationFn: (body: { id: string; enabled: boolean }) => api.triggers.update(body.id, { enabled: body.enabled }),
    onSuccess: (_, body) => {
      queryClient.invalidateQueries({ queryKey: ["workflow", id] });
      toast.success(body.enabled ? "Trigger enabled." : "Trigger disabled.");
    },
    onError: (error) => toast.error(`Trigger update failed — ${String(error)}`),
  });

  const exportWorkflow = async () => {
    try {
      const doc = await api.workflows.export(id);
      const blob = new Blob([JSON.stringify(doc, null, 2)], { type: "application/json" });
      const anchor = document.createElement("a");
      anchor.href = URL.createObjectURL(blob);
      anchor.download = `${workflow?.name ?? "workflow"}.workflow.json`;
      anchor.click();
      URL.revokeObjectURL(anchor.href);
    } catch (exportError) {
      toast.error(`Export failed — ${String(exportError)}`);
    }
  };

  const removeWorkflow = useMutation({
    mutationFn: () => api.workflows.remove(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["workflows"] });
      toast.success("Workflow and its execution history deleted.");
      navigate("/workflows");
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
        <a href="/workflows" className="text-emerald-400 hover:underline">
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
        <div className="flex items-center gap-2">
          <button
            type="button"
            onClick={() => setRunOpen(true)}
            className="rounded-md bg-emerald-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-emerald-500"
          >
            Run now
          </button>
          <DropdownMenu>
            <DropdownMenuTrigger asChild>
              <button
                type="button"
                aria-label="More actions"
                className="rounded-md border border-zinc-700 px-2.5 py-1.5 text-sm text-zinc-300 hover:bg-zinc-900"
              >
                ⋯
              </button>
            </DropdownMenuTrigger>
            <DropdownMenuContent>
              <DropdownMenuItem onSelect={() => navigate(`/workflows/${id}/edit`)}>Edit</DropdownMenuItem>
              <DropdownMenuItem onSelect={exportWorkflow}>Export</DropdownMenuItem>
              <DropdownMenuItem
                destructive
                onSelect={async () => {
                  if (
                    await confirm({
                      title: "Delete workflow?",
                      body: `Delete "${workflow.name}" and its entire execution history?`,
                      confirmLabel: "Delete",
                      destructive: true,
                    })
                  ) {
                    removeWorkflow.mutate();
                  }
                }}
              >
                Delete
              </DropdownMenuItem>
            </DropdownMenuContent>
          </DropdownMenu>
        </div>
      </div>
      {removeWorkflow.error && <p className="text-sm text-red-400">{String(removeWorkflow.error)}</p>}

      <Dialog open={runOpen} onOpenChange={setRunOpen}>
        <DialogContent title="Run now">
          <p className="mb-2 text-xs text-zinc-500">
            Optional JSON payload — available in steps as <code className="text-zinc-400">{"{{trigger.payload}}"}</code>.
          </p>
          <textarea
            className="w-full rounded-md border border-zinc-800 bg-zinc-900 px-3 py-1.5 font-mono text-xs placeholder:text-zinc-600 focus:border-emerald-500 focus:outline-none"
            rows={4}
            placeholder='{ }'
            value={payload}
            onChange={(e) => setPayload(e.target.value)}
          />
          {execute.error && <p className="mt-1 text-sm text-red-400">{String(execute.error)}</p>}
          <button
            type="button"
            onClick={() => execute.mutate()}
            disabled={execute.isPending}
            className="mt-3 w-full rounded-md bg-emerald-600 px-3 py-2 text-sm font-medium text-white hover:bg-emerald-500 disabled:opacity-50"
          >
            {execute.isPending ? "Starting…" : "Run"}
          </button>
        </DialogContent>
      </Dialog>

      <Dialog open={selectedStepOrder != null} onOpenChange={(open) => { if (!open) setSelectedStepOrder(null); }}>
        <DialogContent
          title={(() => {
            const step = workflow.latestVersion.steps.find((s) => s.order === selectedStepOrder);
            return step ? `#${step.order + 1} ${step.name ?? step.actionType}` : "Step";
          })()}
        >
          {(() => {
            const step = workflow.latestVersion.steps.find((s) => s.order === selectedStepOrder);
            if (!step) return null;
            return (
              <>
                <div className="mb-2 text-xs text-zinc-500">{step.actionType}</div>
                <CodeBlock text={prettyJson(step.configJson)} />
              </>
            );
          })()}
        </DialogContent>
      </Dialog>

      <Dialog open={selectedTriggerIndex != null} onOpenChange={(open) => { if (!open) setSelectedTriggerIndex(null); }}>
        <DialogContent
          title={(() => {
            const trigger = selectedTriggerIndex != null ? workflow.triggers[selectedTriggerIndex] : undefined;
            return trigger ? `${trigger.type} trigger` : "Trigger";
          })()}
        >
          {(() => {
            const trigger = selectedTriggerIndex != null ? workflow.triggers[selectedTriggerIndex] : undefined;
            if (!trigger) return null;
            return (
              <div className="space-y-2 text-xs">
                <div className="flex flex-wrap items-center gap-3">
                  <span className={trigger.enabled ? "text-emerald-400" : "text-red-400"}>
                    {trigger.enabled ? "● enabled" : "● disabled"}
                  </span>
                  {trigger.nextRunAt && (
                    <span className="text-zinc-500">next: {new Date(trigger.nextRunAt).toLocaleString()}</span>
                  )}
                  {trigger.lastFiredAt && (
                    <span className="text-zinc-500">last: {new Date(trigger.lastFiredAt).toLocaleString()}</span>
                  )}
                </div>
                {trigger.lastError && <p className="text-amber-400/80">⚠ {trigger.lastError}</p>}
                <CodeBlock text={prettyJson(trigger.configJson)} />
              </div>
            );
          })()}
        </DialogContent>
      </Dialog>

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
          Overview <span className="text-zinc-500">(v{workflow.latestVersion.version})</span>
        </h2>
        <div className="mb-4">
          <WorkflowGraph
            steps={[...workflow.latestVersion.steps]
              .sort((a, b) => a.order - b.order)
              .map((s) => ({ key: s.order, label: s.name ?? s.actionType, actionType: s.actionType }))}
            triggers={workflow.triggers.map((t, i) => ({
              key: i,
              label: triggerNodeLabel(t),
              entryStepKey: t.entryStepOrder ?? undefined,
            }))}
            stepEdges={
              workflow.latestVersion.edges.length > 0
                ? workflow.latestVersion.edges.map((e) => ({ sourceKey: e.from, targetKey: e.to, label: e.label }))
                : backboneEdges(
                    [...workflow.latestVersion.steps].sort((a, b) => a.order - b.order).map((s) => s.order),
                  )
            }
            selection={selectedTriggerIndex != null ? `trigger:${selectedTriggerIndex}` : selectedStepOrder}
            onSelect={(sel) => {
              if (typeof sel === "number") {
                setSelectedStepOrder(sel);
                setSelectedTriggerIndex(null);
              } else if (typeof sel === "string" && sel.startsWith("trigger:")) {
                setSelectedTriggerIndex(Number(sel.slice("trigger:".length)));
                setSelectedStepOrder(null);
              }
            }}
            height="20rem"
          />
          <p className="mt-1 text-[11px] text-zinc-600">
            Triggers feed the first step; branches show their labels. Edit steps &amp; config in the builder.
          </p>
        </div>
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
                    onClick={async () => {
                      if (
                        await confirm({
                          title: `Restore v${version.version}?`,
                          body: `Its steps become v${workflow.latestVersion.version + 1} — nothing is rewritten.`,
                          confirmLabel: "Restore",
                        })
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
            <li key={trigger.id} className="rounded-lg border border-zinc-800 px-4 py-2.5 text-sm">
              <div className="flex items-center justify-between">
                <div className="flex items-center gap-3">
                  <span className="font-medium">{trigger.type}</span>
                  {!trigger.enabled && <span className="text-xs text-red-400">disabled</span>}
                  {trigger.lastError && (
                    <span className="text-xs text-amber-400" title={trigger.lastError}>
                      ⚠ failing
                    </span>
                  )}
                  {trigger.type === "webhook" && (
                    <code className="text-xs text-zinc-500">POST /api/webhooks/{trigger.id}?secret=•••</code>
                  )}
                  {trigger.type === "workflow" && (
                    <ChainSummary configJson={trigger.configJson} workflows={allWorkflows} />
                  )}
                  <TriggerSummary type={trigger.type} configJson={trigger.configJson} />
                  {trigger.nextRunAt && (
                    <span className="text-xs text-zinc-500">
                      next: {new Date(trigger.nextRunAt).toLocaleString()}
                    </span>
                  )}
                </div>
                <div className="flex gap-3">
                  <button
                    type="button"
                    onClick={() => updateTrigger.mutate({ id: trigger.id, enabled: !trigger.enabled })}
                    className="text-xs text-zinc-500 hover:text-emerald-400"
                  >
                    {trigger.enabled ? "Disable" : "Enable"}
                  </button>
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
              </div>
              {trigger.lastError && (
                <p
                  className="mt-1.5 text-xs text-amber-400/80"
                  title={trigger.lastErrorAt ? `Last failed ${new Date(trigger.lastErrorAt).toLocaleString()}` : undefined}
                >
                  ⚠ {trigger.lastError}
                </p>
              )}
            </li>
          ))}
          {workflow.triggers.length === 0 && (
            <li className="text-sm text-zinc-500">No triggers — runs manually only.</li>
          )}
        </ul>

        <p className="text-xs text-zinc-600">
          Add or change triggers in the{" "}
          <Link to={`/workflows/${id}/edit`} className="text-emerald-400 hover:underline">
            builder
          </Link>
          . Enable/disable, rotate a webhook secret, or delete them here.
        </p>
      </section>

      <Dialog open={newWebhook != null} onOpenChange={(open) => { if (!open) setNewWebhook(null); }}>
        <DialogContent title="Webhook URL">
          {newWebhook && (() => {
            const url = `POST ${newWebhook.startsWith("http") ? newWebhook : `${window.location.origin}${newWebhook}`}`;
            return (
              <div className="space-y-3">
                <p className="text-xs text-amber-500/90">⚠ Copy this now — the secret is shown only once. If you lose it, rotate the secret to get a new one.</p>
                <code className="block break-all rounded-md border border-zinc-800 bg-zinc-950 p-2 text-xs text-zinc-300">{url}</code>
                <button
                  type="button"
                  onClick={() => {
                    navigator.clipboard?.writeText(url.replace(/^POST /, "")).then(
                      () => toast.success("Copied."),
                      () => toast.error("Copy failed — select and copy manually."),
                    );
                  }}
                  className="w-full rounded-md bg-emerald-600 px-3 py-2 text-sm font-medium text-white hover:bg-emerald-500"
                >
                  Copy URL
                </button>
              </div>
            );
          })()}
        </DialogContent>
      </Dialog>

      <WorkflowStateSection workflowId={id} triggers={workflow.triggers} />
    </div>
  );
}

// Friendly label for a state group's owning trigger: type + its url/cron.
function triggerLabel(trigger: WorkflowTrigger | undefined): string {
  if (!trigger) return "unknown / deleted trigger";
  let config: Record<string, unknown> = {};
  try {
    config = JSON.parse(trigger.configJson) as Record<string, unknown>;
  } catch {
    // ignore
  }
  const detail = typeof config.url === "string" ? config.url : typeof config.cron === "string" ? config.cron : "";
  return detail ? `${trigger.type} · ${detail}` : trigger.type;
}

// Durable per-workflow KV state (feed dedup, cursors). Keys are namespaced
// `trigger:<id>:…`, so group by owning trigger and collapse the long dedup sets
// instead of dumping every row. Only shows when there's state.
function WorkflowStateSection({ workflowId, triggers }: { workflowId: string; triggers: WorkflowTrigger[] }) {
  const queryClient = useQueryClient();
  const confirm = useConfirm();
  const { data: entries } = useQuery({
    queryKey: ["workflow-state", workflowId],
    queryFn: () => api.workflows.state(workflowId),
  });

  const invalidate = () => queryClient.invalidateQueries({ queryKey: ["workflow-state", workflowId] });

  const clear = useMutation({
    mutationFn: (prefix?: string) => api.workflows.clearState(workflowId, prefix),
    onSuccess: () => {
      invalidate();
      toast.success("State cleared — will re-process from scratch.");
    },
    onError: (error) => toast.error(`Clear failed — ${String(error)}`),
  });

  const setEntry = useMutation({
    mutationFn: ({ key, value }: { key: string; value: string }) => api.workflows.setState(workflowId, key, value),
    onSuccess: invalidate,
    onError: (error) => toast.error(`Save failed — ${String(error)}`),
  });

  const removeEntry = useMutation({
    mutationFn: (key: string) => api.workflows.removeState(workflowId, key),
    onSuccess: invalidate,
    onError: (error) => toast.error(`Delete failed — ${String(error)}`),
  });

  const [editingKey, setEditingKey] = useState<string | null>(null);
  const [draft, setDraft] = useState("");

  if (!entries || entries.length === 0) return null;

  // Group by the `trigger:<id>:` prefix; the remainder is the entry's own key.
  const groups = new Map<string, { triggerId: string | null; rows: { key: string; rest: string; value: string; expiresAt: string | null }[] }>();
  for (const entry of entries) {
    const match = /^trigger:([^:]+):(.+)$/.exec(entry.key);
    const triggerId = match ? match[1] : null;
    const rest = match ? match[2] : entry.key;
    const groupKey = triggerId ?? "__other__";
    const group = groups.get(groupKey) ?? { triggerId, rows: [] };
    group.rows.push({ key: entry.key, rest, value: entry.value, expiresAt: entry.expiresAt });
    groups.set(groupKey, group);
  }

  return (
    <section>
      <div className="mb-3 flex items-center justify-between">
        <h2 className="text-sm font-medium text-zinc-300">
          State <span className="text-zinc-500">({entries.length})</span>
        </h2>
        <button
          type="button"
          onClick={async () => {
            if (await confirm({ title: "Clear all state?", body: "Feeds will re-process from scratch.", confirmLabel: "Clear", destructive: true })) {
              clear.mutate(undefined);
            }
          }}
          disabled={clear.isPending}
          className="text-xs text-zinc-500 hover:text-red-400 disabled:opacity-50"
        >
          Clear all
        </button>
      </div>

      <div className="space-y-2">
        {[...groups.values()].map((group) => {
          const trigger = group.triggerId ? triggers.find((t) => t.id === group.triggerId) : undefined;
          return (
            <details key={group.triggerId ?? "other"} className="rounded-lg border border-zinc-800">
              <summary className="flex cursor-pointer items-center justify-between gap-3 px-4 py-2 text-sm">
                <span className="truncate text-zinc-200">
                  {group.triggerId ? triggerLabel(trigger) : "other"}
                </span>
                <span className="flex shrink-0 items-center gap-3 text-xs text-zinc-500">
                  <span>{group.rows.length} entries</span>
                  {group.triggerId && (
                    <button
                      type="button"
                      title="Reset this feed's dedup"
                      onClick={(e) => {
                        e.preventDefault();
                        e.stopPropagation();
                        confirm({ title: "Reset this feed?", body: "Its items will re-process from scratch.", confirmLabel: "Reset", destructive: true }).then((ok) => {
                          if (ok) clear.mutate(`trigger:${group.triggerId}:`);
                        });
                      }}
                      className="hover:text-red-400"
                    >
                      Clear feed
                    </button>
                  )}
                </span>
              </summary>
              <ul className="max-h-72 divide-y divide-zinc-900 overflow-y-auto border-t border-zinc-800">
                {group.rows.map((row) => (
                  <li key={row.key} className="flex items-center justify-between gap-3 px-4 py-1.5 text-xs">
                    <code className="min-w-0 flex-1 truncate text-zinc-400">{row.rest}</code>
                    <span className="flex shrink-0 items-center gap-1.5">
                      {editingKey === row.key ? (
                        <>
                          <input
                            autoFocus
                            value={draft}
                            onChange={(e) => setDraft(e.target.value)}
                            className="w-28 rounded border border-zinc-700 bg-zinc-900 px-1.5 py-0.5 text-xs focus:border-emerald-500 focus:outline-none"
                          />
                          <button
                            type="button"
                            title="Save"
                            onClick={() => {
                              setEntry.mutate({ key: row.key, value: draft });
                              setEditingKey(null);
                            }}
                            className="text-emerald-400 hover:text-emerald-300"
                          >
                            ✓
                          </button>
                          <button type="button" title="Cancel" onClick={() => setEditingKey(null)} className="text-zinc-500 hover:text-zinc-200">
                            ✕
                          </button>
                        </>
                      ) : (
                        <>
                          <button
                            type="button"
                            title="Edit value"
                            onClick={() => {
                              setEditingKey(row.key);
                              setDraft(row.value);
                            }}
                            className="max-w-[8rem] truncate font-mono text-zinc-500 hover:text-zinc-200"
                          >
                            {row.value}
                          </button>
                          {row.expiresAt && (
                            <span title={`Expires ${new Date(row.expiresAt).toLocaleString()}`}>⏳</span>
                          )}
                          <button
                            type="button"
                            title="Delete entry"
                            onClick={() => removeEntry.mutate(row.key)}
                            className="text-zinc-600 hover:text-red-400"
                          >
                            🗑
                          </button>
                        </>
                      )}
                    </span>
                  </li>
                ))}
              </ul>
            </details>
          );
        })}
      </div>
    </section>
  );
}

// Inline config preview so trigger rows aren't just a bare type (e.g. two rss
// triggers showing their distinct feed URLs). webhook + workflow have their own.
function TriggerSummary({ type, configJson }: { type: string; configJson: string }) {
  if (type === "webhook" || type === "workflow") return null;

  let config: Record<string, unknown> = {};
  try {
    config = JSON.parse(configJson) as Record<string, unknown>;
  } catch {
    return null;
  }

  if (type === "cron" && typeof config.cron === "string") {
    return <code className="text-xs text-zinc-500">⏰ {config.cron}</code>;
  }

  if ((type === "rss" || type === "http.poll") && typeof config.url === "string") {
    return (
      <span className="max-w-md truncate text-xs text-zinc-500" title={config.url}>
        🔗 {config.url}
      </span>
    );
  }

  // Generic plugin trigger: a couple of primitive fields, skipping templated secrets.
  const entries = Object.entries(config)
    .filter(([, v]) => typeof v === "string" || typeof v === "number" || typeof v === "boolean")
    .filter(([, v]) => !(typeof v === "string" && v.includes("{{")))
    .slice(0, 3)
    .map(([k, v]) => `${k}: ${typeof v === "string" && v.length > 30 ? `${v.slice(0, 30)}…` : v}`);

  return entries.length > 0 ? (
    <span className="max-w-md truncate text-xs text-zinc-500">{entries.join(" · ")}</span>
  ) : null;
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

