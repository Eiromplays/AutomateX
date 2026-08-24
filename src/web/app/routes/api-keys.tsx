import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { toast } from "../components/toast";
import { useConfirm } from "../components/ui/confirm";
import { Dialog, DialogContent, DialogHeader, DialogTitle } from "../components/ui/dialog";
import {
  api,
  type ApiKeyScope,
  type ApiKeySummary,
  type CreatedApiKey,
  DEFAULT_WORKSPACE_ID,
  getWorkspaceId,
} from "../lib/api";
import { buildCreateKeyBody, describeKeyTarget, type KeyFormState } from "../lib/api-keys-form";

const inputClass =
  "rounded-md border border-line-strong bg-card px-3 py-1.5 text-sm " +
  "placeholder:text-fg-placeholder focus:border-brand-bright focus:outline-none";

const fmtDate = (value: string | null) => (value ? new Date(value).toLocaleDateString() : "—");
const fmtDateTime = (value: string | null) => (value ? new Date(value).toLocaleString() : "never");

export default function ApiKeys() {
  const queryClient = useQueryClient();
  const confirm = useConfirm();

  const { data: me } = useQuery({ queryKey: ["auth", "me"], queryFn: api.auth.me, staleTime: Infinity });
  const { data: workspaces } = useQuery({ queryKey: ["workspaces"], queryFn: api.workspaces.list });
  const { data: workflows } = useQuery({ queryKey: ["workflows"], queryFn: api.workflows.list });

  const currentId = getWorkspaceId() ?? DEFAULT_WORKSPACE_ID;
  const current = workspaces?.find((w) => w.id === currentId) ?? workspaces?.[0];
  const isAdmin = me?.isInstanceAdmin ?? false;
  const canManage = isAdmin || current?.role === "Owner";

  const scopeOptions: ApiKeyScope[] = isAdmin ? ["Instance", "Workspace", "Workflow"] : ["Workspace", "Workflow"];

  const [form, setForm] = useState<KeyFormState>({
    name: "",
    scope: "Workspace",
    workspaceId: "",
    workflowId: "",
    role: "Viewer",
    expiresAt: "",
    rateLimitPerMinute: "",
    ipAllowlist: "",
  });
  const [created, setCreated] = useState<CreatedApiKey | null>(null);

  const { data: keys, error: listError } = useQuery({
    queryKey: ["apikeys"],
    queryFn: api.apiKeys.list,
    enabled: canManage,
  });

  const create = useMutation({
    mutationFn: () => {
      const built = buildCreateKeyBody({ ...form, workspaceId: current?.id ?? "" });
      if ("error" in built) throw new Error(built.error);
      return api.apiKeys.create(built);
    },
    onSuccess: (key) => {
      setCreated(key);
      setForm((f) => ({ ...f, name: "", workflowId: "", expiresAt: "", rateLimitPerMinute: "", ipAllowlist: "" }));
      queryClient.invalidateQueries({ queryKey: ["apikeys"] });
    },
    onError: (error) => toast.error(`Create failed — ${String(error)}`),
  });

  const revoke = useMutation({
    mutationFn: (id: string) => api.apiKeys.revoke(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["apikeys"] });
      toast.success("Key revoked.");
    },
    onError: (error) => toast.error(`Revoke failed — ${String(error)}`),
  });

  const workflowName = (id: string | null) => workflows?.find((w) => w.id === id)?.name ?? null;
  const workspaceName = (id: string | null) => workspaces?.find((w) => w.id === id)?.name ?? null;

  return (
    <div className="max-w-3xl space-y-6">
      <div>
        <h1 className="text-lg font-semibold">API keys</h1>
        <p className="mt-1 text-sm text-muted-foreground">
          Scoped, hashed, revocable credentials for scripts, integrations, and MCP.
        </p>
      </div>

      <p className="rounded-md border border-line bg-card/50 px-3 py-2 text-xs text-muted-foreground">
        The shared <code className="text-muted-foreground">Auth__ApiKey</code> was removed in v4.6. API and script
        access now uses the scoped keys below — send one as the <code className="text-muted-foreground">X-Api-Key</code>{" "}
        header, or sign in with it via the ⚿ button. The full token is shown once at creation.
      </p>

      {!canManage ? (
        <p className="rounded-lg border border-line px-4 py-6 text-center text-sm text-muted-foreground">
          Only workspace Owners and instance admins can manage API keys.
        </p>
      ) : (
        <>
          <section className="space-y-3 rounded-lg border border-line p-4">
            <h2 className="text-sm font-medium text-fg-body">Create a key</h2>
            <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
              <input
                className={inputClass}
                placeholder="Name (e.g. grafana-readonly)"
                value={form.name}
                onChange={(e) => setForm({ ...form, name: e.target.value })}
              />
              <select
                className={inputClass}
                value={form.scope}
                onChange={(e) => setForm({ ...form, scope: e.target.value as ApiKeyScope })}
              >
                {scopeOptions.map((scope) => (
                  <option key={scope} value={scope}>
                    {scope}
                  </option>
                ))}
              </select>

              {form.scope !== "Instance" && (
                <select
                  className={inputClass}
                  value={form.role}
                  onChange={(e) => setForm({ ...form, role: e.target.value })}
                >
                  <option>Viewer</option>
                  <option>Editor</option>
                  <option>Owner</option>
                </select>
              )}

              {form.scope === "Workflow" && (
                <select
                  className={inputClass}
                  value={form.workflowId}
                  onChange={(e) => setForm({ ...form, workflowId: e.target.value })}
                >
                  <option value="">Select a workflow…</option>
                  {workflows?.map((wf) => (
                    <option key={wf.id} value={wf.id}>
                      {wf.name}
                    </option>
                  ))}
                </select>
              )}

              <label className="flex items-center gap-2 text-xs text-muted-foreground">
                Expires
                <input
                  type="date"
                  className={`${inputClass} flex-1`}
                  value={form.expiresAt}
                  onChange={(e) => setForm({ ...form, expiresAt: e.target.value })}
                />
              </label>

              <label className="flex items-center gap-2 text-xs text-muted-foreground">
                Rate limit
                <input
                  type="number"
                  min={1}
                  className={`${inputClass} flex-1`}
                  placeholder="req/min (blank = unlimited)"
                  value={form.rateLimitPerMinute}
                  onChange={(e) => setForm({ ...form, rateLimitPerMinute: e.target.value })}
                />
              </label>
            </div>

            <label className="block text-xs text-muted-foreground">
              IP allowlist <span className="text-muted-foreground">— one CIDR or IP per line; blank = any address</span>
              <textarea
                className={`${inputClass} mt-1 h-16 w-full font-mono`}
                placeholder={"203.0.113.0/24\n198.51.100.7"}
                value={form.ipAllowlist}
                onChange={(e) => setForm({ ...form, ipAllowlist: e.target.value })}
              />
            </label>

            <div className="flex items-center justify-between">
              <p className="text-xs text-muted-foreground">
                {form.scope === "Instance"
                  ? "Instance keys are admin everywhere; role is ignored."
                  : `Scoped to ${current?.name ?? "this workspace"}.`}
              </p>
              <button
                type="button"
                disabled={!form.name.trim() || create.isPending}
                onClick={() => create.mutate()}
                className="rounded-md bg-primary px-3 py-1.5 text-sm font-medium text-primary-foreground hover:bg-brand-bright disabled:opacity-50"
              >
                {create.isPending ? "Creating…" : "Create key"}
              </button>
            </div>
          </section>

          <section className="space-y-3">
            <h2 className="text-sm font-medium text-fg-body">
              Keys {isAdmin ? "(all workspaces)" : `in ${current?.name ?? "this workspace"}`}
            </h2>
            {listError && <p className="text-sm text-danger">{String(listError)}</p>}
            <ul className="divide-y divide-zinc-800 rounded-lg border border-line">
              {keys?.map((key: ApiKeySummary) => {
                const revoked = key.revokedAt !== null;
                const expired = key.expiresAt !== null && new Date(key.expiresAt) < new Date();
                return (
                  <li key={key.id} className="flex items-center justify-between gap-4 px-4 py-3 text-sm">
                    <div className="min-w-0 space-y-1">
                      <div className="flex items-center gap-2">
                        <span className={revoked ? "text-muted-foreground line-through" : ""}>{key.name}</span>
                        <span className="rounded bg-muted px-1.5 py-0.5 text-xs text-muted-foreground">{key.role}</span>
                        {revoked && <span className="text-xs text-muted-foreground">revoked</span>}
                        {!revoked && expired && <span className="text-xs text-warning">expired</span>}
                      </div>
                      <div className="truncate text-xs text-muted-foreground">
                        {describeKeyTarget(key.scope, workflowName(key.workflowId), workspaceName(key.workspaceId))}
                        {" · "}
                        <code className="text-muted-foreground">atx_{key.prefix}…</code>
                      </div>
                      <div className="text-xs text-muted-foreground">
                        last used {fmtDateTime(key.lastUsedAt)} · expires {fmtDate(key.expiresAt)} · by {key.createdBy}
                      </div>
                      {(key.rateLimitPerMinute !== null || key.ipAllowlist.length > 0) && (
                        <div className="text-xs text-muted-foreground">
                          {key.rateLimitPerMinute !== null && <span>{key.rateLimitPerMinute} req/min</span>}
                          {key.rateLimitPerMinute !== null && key.ipAllowlist.length > 0 && " · "}
                          {key.ipAllowlist.length > 0 && (
                            <span>
                              IPs <code className="text-muted-foreground">{key.ipAllowlist.join(", ")}</code>
                            </span>
                          )}
                        </div>
                      )}
                    </div>
                    {!revoked && (
                      <button
                        type="button"
                        onClick={async () => {
                          if (
                            await confirm({
                              title: `Revoke "${key.name}"?`,
                              body: "Any client using this key stops working immediately. This can't be undone.",
                              confirmLabel: "Revoke",
                              destructive: true,
                            })
                          ) {
                            revoke.mutate(key.id);
                          }
                        }}
                        className="shrink-0 text-xs text-muted-foreground hover:text-danger"
                      >
                        Revoke
                      </button>
                    )}
                  </li>
                );
              })}
              {keys?.length === 0 && (
                <li className="px-4 py-6 text-center text-sm text-muted-foreground">No keys yet.</li>
              )}
            </ul>
          </section>
        </>
      )}

      <Dialog open={created !== null} onOpenChange={(open) => !open && setCreated(null)}>
        {created && (
          <DialogContent>
            <DialogHeader>
              <DialogTitle>Copy your API key</DialogTitle>
            </DialogHeader>
            <div className="space-y-3">
              <p className="text-sm text-muted-foreground">
                This is the only time <span className="font-medium text-fg-emphasis">{created.name}</span> is shown.
                Store it now — it can't be retrieved again.
              </p>
              <code className="block w-full break-all rounded-md border border-line-strong bg-background px-3 py-2 text-xs text-emerald-300">
                {created.token}
              </code>
              <div className="flex justify-end gap-2">
                <button
                  type="button"
                  onClick={() => {
                    navigator.clipboard?.writeText(created.token).then(
                      () => toast.success("Copied to clipboard."),
                      () => toast.error("Couldn't copy — select and copy manually."),
                    );
                  }}
                  className="rounded-md bg-primary px-3 py-1.5 text-sm font-medium text-primary-foreground hover:bg-brand-bright"
                >
                  Copy
                </button>
                <button
                  type="button"
                  onClick={() => setCreated(null)}
                  className="rounded-md border border-line-strong px-3 py-1.5 text-sm hover:bg-card"
                >
                  Done
                </button>
              </div>
            </div>
          </DialogContent>
        )}
      </Dialog>
    </div>
  );
}
