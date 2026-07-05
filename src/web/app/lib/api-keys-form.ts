import type { ApiKeyScope, CreateApiKeyBody } from "./api";

export type KeyFormState = {
  name: string;
  scope: ApiKeyScope;
  workspaceId: string; // the current workspace (targets Workspace/Workflow keys)
  workflowId: string; // required only for a Workflow-scoped key
  role: string; // ignored for Instance scope (server pins it to Owner)
  expiresAt: string; // <input type="date"> value; "" = never
};

// Builds the create-key request body from the form, or an error message when the targeting is
// incomplete for the chosen scope. Kept pure so the per-scope rules are unit-testable.
export function buildCreateKeyBody(form: KeyFormState): CreateApiKeyBody | { error: string } {
  const name = form.name.trim();
  if (!name) {
    return { error: "Name is required." };
  }

  if ((form.scope === "Workspace" || form.scope === "Workflow") && !form.workspaceId) {
    return { error: "Pick a workspace before creating a scoped key." };
  }

  if (form.scope === "Workflow" && !form.workflowId) {
    return { error: "A Workflow-scoped key needs a workflow." };
  }

  return {
    name,
    scope: form.scope,
    workspaceId: form.scope === "Instance" ? null : form.workspaceId,
    workflowId: form.scope === "Workflow" ? form.workflowId : null,
    role: form.scope === "Instance" ? null : form.role,
    // A bare date means "expires at the end of that day, UTC"; empty means never.
    expiresAt: form.expiresAt ? new Date(`${form.expiresAt}T23:59:59Z`).toISOString() : null,
  };
}

// Human-readable target for the list ("Instance", a workflow name, or a short workspace id).
export function describeKeyTarget(
  scope: ApiKeyScope,
  workflowName: string | null,
  workspaceName: string | null,
): string {
  if (scope === "Instance") {
    return "Instance (all workspaces)";
  }
  if (scope === "Workflow") {
    return `Workflow · ${workflowName ?? "(unknown)"}`;
  }
  return `Workspace · ${workspaceName ?? "(unknown)"}`;
}
