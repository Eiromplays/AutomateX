const API = "/api";
const WORKSPACE_STORAGE = "automatex.workspace";

export function getWorkspaceId(): string | null {
  return localStorage.getItem(WORKSPACE_STORAGE);
}

export function setWorkspaceId(id: string | null): void {
  if (id) {
    localStorage.setItem(WORKSPACE_STORAGE, id);
  } else {
    localStorage.removeItem(WORKSPACE_STORAGE);
  }
}

// FastEndpoints wraps validation failures in an errors envelope — unwrap to the
// human sentences so toasts and confirms read cleanly.
export function extractErrorMessage(body: string): string {
  try {
    const parsed = JSON.parse(body) as { errors?: Record<string, string[]>; message?: string };
    const all = parsed.errors ? Object.values(parsed.errors).flat() : [];
    if (all.length > 0) return all.join(" ");
    if (parsed.message) return parsed.message;
  } catch {
    // not JSON — fall through to the raw body
  }
  return body;
}

// Auth is an HttpOnly session cookie set by POST /auth/session — the key never
// touches JS-readable storage and rides the SignalR handshake automatically.
async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const workspace = getWorkspaceId();
  const response = await fetch(`${API}${path}`, {
    headers: {
      "Content-Type": "application/json",
      ...(workspace ? { "X-Workspace-Id": workspace } : {}),
    },
    ...init,
  });
  if (response.status === 401) {
    throw new Error("401: API key required — sign in via the ⚿ button in the header.");
  }
  if (!response.ok) {
    throw new Error(extractErrorMessage(await response.text()));
  }
  return response.status === 204 ? (undefined as T) : ((await response.json()) as T);
}

export type WorkflowStateEntry = { key: string; value: string; expiresAt: string | null; updatedAt: string };

export type WorkflowSummary = {
  id: string;
  name: string;
  description: string | null;
  createdAt: string;
  latestVersion: number;
  runsAfter: string[];
  feeds: string[];
};

export type WorkflowStep = {
  id: string;
  order: number;
  name: string | null;
  actionType: string;
  configJson: string;
};

export type WorkflowTrigger = {
  id: string;
  type: string;
  enabled: boolean;
  nextRunAt: string | null;
  lastFiredAt: string | null;
  lastError: string | null;
  lastErrorAt: string | null;
  configJson: string;
};

export type WorkflowVersionSummary = {
  id: string;
  version: number;
  createdAt: string;
  stepCount: number;
};

export type ChainLink = {
  workflowId: string;
  name: string;
  on: string;
};

export type WorkflowDetail = {
  id: string;
  name: string;
  description: string | null;
  createdAt: string;
  latestVersion: { id: string; version: number; createdAt: string; steps: WorkflowStep[] };
  versions: WorkflowVersionSummary[];
  triggers: WorkflowTrigger[];
  runsAfter: ChainLink[];
  feeds: ChainLink[];
};

export type ActionDescriptor = {
  type: string;
  displayName: string;
  description: string | null;
  source: string;
  configSchema: string | null;
  resultSchema: string | null;
};

export type ExecutionSummary = {
  id: string;
  workflowId: string;
  workflowName: string;
  workflowVersionId: string;
  triggeredBy: string;
  status: string;
  startedAt: string;
  completedAt: string | null;
  parentExecutionId: string | null;
};

export type ChainedExecution = {
  executionId: string;
  workflowId: string;
  status: string;
};

export type ExecutionStep = {
  id: string;
  stepOrder: number;
  actionType: string;
  status: string;
  attempts: number;
  failedAttempts: number;
  output: string | null;
  error: string | null;
  startedAt: string;
  completedAt: string | null;
};

export type ExecutionDetail = Omit<ExecutionSummary, "workflowName"> & {
  triggerPayload: string | null;
  steps: ExecutionStep[];
  chained: ChainedExecution[];
  retries: ChainedExecution[];
};

export type CreateWorkflowStep = {
  actionType: string;
  name: string | null;
  config: Record<string, unknown>;
};

export type ConnectionSummary = {
  id: string;
  name: string;
  provider: string | null;
  createdAt: string;
  secretKeys: string[];
  decryptable: boolean;
};

export type ConnectionField = {
  key: string;
  label: string;
  secret: boolean;
  required: boolean;
  helpText: string | null;
  docsUrl: string | null;
};

export type ConnectionTypeInfo = {
  type: string;
  displayName: string;
  description: string | null;
  source: string;
  fields: ConnectionField[];
};

export type AuthMe = {
  mode: "open" | "apikey" | "oidc";
  authenticated: boolean;
  name: string | null;
  email: string | null;
};

export type WorkspaceSummary = { id: string; name: string; role: string; isNew: boolean };

export type PluginInfo = {
  name: string;
  version: string;
  fingerprint: string;
  modifiedAt: string | null;
};

export type PluginsOverview = {
  uploadEnabled: boolean;
  global: PluginInfo[];
  workspace: PluginInfo[];
};

export type TriggerTypeInfo = {
  type: string;
  displayName: string;
  description: string | null;
  source: string;
  configSchema: string | null;
};

export type CatalogEntry = {
  name: string;
  version: string;
  description: string | null;
  installed: boolean;
};

export type PluginCatalogInfo = {
  installEnabled: boolean;
  entries: CatalogEntry[];
};

export type PluginUploadResult = {
  name: string;
  scope: string;
  globalPlugins: number;
  workspacePlugins: number;
  previousFingerprint: string | null;
  fingerprint: string | null;
};

export type WorkspaceMember = {
  id: string;
  email: string;
  role: string;
  signedInBefore: boolean;
};

export type DayBucket = { date: string; total: number; succeeded: number; failed: number };
export type WorkflowStat = {
  workflowId: string;
  name: string;
  total: number;
  succeeded: number;
  failed: number;
  avgDurationMs: number | null;
};
export type RecentFailure = { id: string; workflowId: string; name: string; startedAt: string };
export type ExecutionStats = {
  total: number;
  succeeded: number;
  failed: number;
  running: number;
  successRate: number;
  p50DurationMs: number | null;
  p95DurationMs: number | null;
  perDay: DayBucket[];
  topWorkflows: WorkflowStat[];
  recentFailures: RecentFailure[];
};

export const api = {
  workspaces: {
    list: () => request<WorkspaceSummary[]>("/workspaces"),
    create: (name: string) =>
      request<{ id: string; name: string }>("/workspaces", {
        method: "POST",
        body: JSON.stringify({ name }),
      }),
    remove: (id: string) => request<void>(`/workspaces/${id}`, { method: "DELETE" }),
    members: {
      list: (id: string) => request<WorkspaceMember[]>(`/workspaces/${id}/members`),
      upsert: (id: string, email: string, role: string) =>
        request<WorkspaceMember>(`/workspaces/${id}/members`, {
          method: "POST",
          body: JSON.stringify({ email, role }),
        }),
      remove: (id: string, memberId: string) =>
        request<void>(`/workspaces/${id}/members/${memberId}`, { method: "DELETE" }),
    },
  },
  connections: {
    list: () => request<ConnectionSummary[]>("/connections"),
    types: () => request<ConnectionTypeInfo[]>("/connection-types"),
    create: (body: { name: string; provider: string | null; secrets: Record<string, string> }) =>
      request<{ id: string; name: string }>("/connections", {
        method: "POST",
        body: JSON.stringify(body),
      }),
    // Merge semantics: value overwrites, null deletes, absent keys stay untouched.
    update: (id: string, body: { provider: string | null; secrets: Record<string, string | null> }) =>
      request<{ id: string; name: string; secretKeys: string[] }>(`/connections/${id}`, {
        method: "PUT",
        body: JSON.stringify(body),
      }),
    remove: (id: string, force = false) =>
      request<void>(`/connections/${id}${force ? "?force=true" : ""}`, { method: "DELETE" }),
    test: (id: string) => request<{ ok: boolean; message: string }>(`/connections/${id}/test`, { method: "POST" }),
  },
  auth: {
    me: () => request<AuthMe>("/auth/me"),
    login: (key: string) =>
      request<void>("/auth/session", { method: "POST", body: JSON.stringify({ key }) }),
    logout: () => request<void>("/auth/session", { method: "DELETE" }),
  },
  actions: {
    list: () => request<ActionDescriptor[]>("/actions"),
  },
  plugins: {
    list: () => request<PluginsOverview>("/plugins"),
    reload: () =>
      request<{ globalPlugins: number; workspacePlugins: number }>("/actions/reload", {
        method: "POST",
      }),
    // Multipart upload — no JSON content type, but the workspace header still rides along.
    upload: async (scope: "global" | "workspace", file: File): Promise<PluginUploadResult> => {
      const form = new FormData();
      form.append("file", file);
      const workspace = getWorkspaceId();
      const response = await fetch(`${API}/plugins/${scope}`, {
        method: "POST",
        body: form,
        headers: workspace ? { "X-Workspace-Id": workspace } : {},
      });
      if (!response.ok) {
        throw new Error(extractErrorMessage(await response.text()));
      }
      return (await response.json()) as PluginUploadResult;
    },
    catalog: () => request<PluginCatalogInfo>("/plugins/catalog"),
    installFromCatalog: (name: string) =>
      request<{ name: string; version: string; previousFingerprint: string | null; fingerprint: string | null }>(
        "/plugins/catalog/install",
        { method: "POST", body: JSON.stringify({ name }) },
      ),
    remove: (scope: "global" | "workspace", name: string, force = false) =>
      request<void>(`/plugins/${scope}/${encodeURIComponent(name)}${force ? "?force=true" : ""}`, {
        method: "DELETE",
      }),
  },
  workflows: {
    list: () => request<WorkflowSummary[]>("/workflows"),
    get: (id: string) => request<WorkflowDetail>(`/workflows/${id}`),
    create: (body: { name: string; description: string | null; steps: CreateWorkflowStep[] }) =>
      request<{ id: string; versionId: string; version: number }>("/workflows", {
        method: "POST",
        body: JSON.stringify(body),
      }),
    // Appends an immutable new version — past executions keep the version they ran.
    update: (id: string, body: { name: string; description: string | null; steps: CreateWorkflowStep[] }) =>
      request<{ id: string; versionId: string; version: number }>(`/workflows/${id}`, {
        method: "PUT",
        body: JSON.stringify(body),
      }),
    remove: (id: string) => request<void>(`/workflows/${id}`, { method: "DELETE" }),
    // Rollback = git revert, not git reset: appends a copy of the target version's steps.
    restoreVersion: (id: string, version: number) =>
      request<{ id: string; versionId: string; version: number }>(
        `/workflows/${id}/versions/${version}/restore`,
        { method: "POST" },
      ),
    // Portable document — secrets excluded by construction (cron triggers only,
    // connections as name references). Import needs same-named connections.
    export: (id: string) => request<Record<string, unknown>>(`/workflows/${id}/export`),
    import: (document: unknown) =>
      request<{ id: string; versionId: string; version: number }>("/workflows/import", {
        method: "POST",
        body: JSON.stringify(document),
      }),
    execute: (id: string, payload?: string) =>
      request<{ executionId: string }>(`/workflows/${id}/execute`, {
        method: "POST",
        ...(payload ? { body: payload } : {}),
      }),
    state: (id: string) => request<WorkflowStateEntry[]>(`/workflows/${id}/state`),
    clearState: (id: string, prefix?: string) =>
      request<void>(`/workflows/${id}/state${prefix ? `?prefix=${encodeURIComponent(prefix)}` : ""}`, {
        method: "DELETE",
      }),
    setState: (id: string, key: string, value: string) =>
      request<void>(`/workflows/${id}/state`, { method: "PUT", body: JSON.stringify({ key, value }) }),
    removeState: (id: string, key: string) =>
      request<void>(`/workflows/${id}/state/entry`, { method: "DELETE", body: JSON.stringify({ key }) }),
  },
  triggers: {
    types: () => request<TriggerTypeInfo[]>("/trigger-types"),
    create: (workflowId: string, body: { type: string; config: Record<string, unknown> }) =>
      request<{
        id: string;
        type: string;
        enabled: boolean;
        nextRunAt: string | null;
        webhookSecret: string | null;
        webhookUrl: string | null;
      }>(`/workflows/${workflowId}/triggers`, { method: "POST", body: JSON.stringify(body) }),
    rotateSecret: (id: string) =>
      request<{ id: string; webhookSecret: string; webhookUrl: string }>(
        `/triggers/${id}/rotate-secret`,
        { method: "POST" },
      ),
    update: (id: string, body: { config?: Record<string, unknown>; enabled?: boolean }) =>
      request<{ id: string; type: string; enabled: boolean; nextRunAt: string | null }>(
        `/triggers/${id}`,
        { method: "PUT", body: JSON.stringify(body) },
      ),
    remove: (id: string) => request<void>(`/triggers/${id}`, { method: "DELETE" }),
  },
  executions: {
    list: () => request<ExecutionSummary[]>("/executions"),
    get: (id: string) => request<ExecutionDetail>(`/executions/${id}`),
    remove: (id: string) => request<void>(`/executions/${id}`, { method: "DELETE" }),
    // Replay with the byte-identical original payload, on the latest version.
    retry: (id: string) =>
      request<{ executionId: string }>(`/executions/${id}/retry`, { method: "POST" }),
  },
  stats: {
    get: (days?: number) =>
      request<ExecutionStats>(`/stats${days ? `?days=${days}` : ""}`),
  },
};
