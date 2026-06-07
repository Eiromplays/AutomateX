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
    throw new Error(`${response.status}: ${await response.text()}`);
  }
  return response.status === 204 ? (undefined as T) : ((await response.json()) as T);
}

export type WorkflowSummary = {
  id: string;
  name: string;
  description: string | null;
  createdAt: string;
  latestVersion: number;
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
};

export type WorkflowVersionSummary = {
  id: string;
  version: number;
  createdAt: string;
  stepCount: number;
};

export type WorkflowDetail = {
  id: string;
  name: string;
  description: string | null;
  createdAt: string;
  latestVersion: { id: string; version: number; createdAt: string; steps: WorkflowStep[] };
  versions: WorkflowVersionSummary[];
  triggers: WorkflowTrigger[];
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
  workflowVersionId: string;
  triggeredBy: string;
  status: string;
  startedAt: string;
  completedAt: string | null;
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

export type ExecutionDetail = ExecutionSummary & {
  triggerPayload: string | null;
  steps: ExecutionStep[];
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

export type AuthMe = {
  mode: "open" | "apikey" | "oidc";
  authenticated: boolean;
  name: string | null;
  email: string | null;
};

export type WorkspaceSummary = { id: string; name: string; role: string; isNew: boolean };

export type WorkspaceMember = {
  id: string;
  email: string;
  role: string;
  signedInBefore: boolean;
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
    remove: (id: string) => request<void>(`/connections/${id}`, { method: "DELETE" }),
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
    execute: (id: string, payload?: string) =>
      request<{ executionId: string }>(`/workflows/${id}/execute`, {
        method: "POST",
        ...(payload ? { body: payload } : {}),
      }),
  },
  triggers: {
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
    remove: (id: string) => request<void>(`/triggers/${id}`, { method: "DELETE" }),
  },
  executions: {
    list: () => request<ExecutionSummary[]>("/executions"),
    get: (id: string) => request<ExecutionDetail>(`/executions/${id}`),
    remove: (id: string) => request<void>(`/executions/${id}`, { method: "DELETE" }),
  },
};
