const API = "/api";

// Auth is an HttpOnly session cookie set by POST /auth/session — the key never
// touches JS-readable storage and rides the SignalR handshake automatically.
async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`${API}${path}`, {
    headers: { "Content-Type": "application/json" },
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

export type WorkflowDetail = {
  id: string;
  name: string;
  description: string | null;
  createdAt: string;
  latestVersion: { id: string; version: number; createdAt: string; steps: WorkflowStep[] };
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

export type ExecutionDetail = ExecutionSummary & { steps: ExecutionStep[] };

export type CreateWorkflowStep = {
  actionType: string;
  name: string | null;
  config: Record<string, unknown>;
};

export const api = {
  auth: {
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
    execute: (id: string) =>
      request<{ executionId: string }>(`/workflows/${id}/execute`, { method: "POST" }),
  },
  triggers: {
    create: (workflowId: string, body: { type: string; config: Record<string, unknown> }) =>
      request<{ id: string; type: string; enabled: boolean; nextRunAt: string | null }>(
        `/workflows/${workflowId}/triggers`,
        { method: "POST", body: JSON.stringify(body) },
      ),
    remove: (id: string) => request<void>(`/triggers/${id}`, { method: "DELETE" }),
  },
  executions: {
    list: () => request<ExecutionSummary[]>("/executions"),
    get: (id: string) => request<ExecutionDetail>(`/executions/${id}`),
  },
};
