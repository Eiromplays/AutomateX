import { describe, expect, it } from "vitest";
import { buildCreateKeyBody, describeKeyTarget, type KeyFormState } from "./api-keys-form";

const base: KeyFormState = {
  name: "grafana",
  scope: "Workspace",
  workspaceId: "ws-1",
  workflowId: "",
  role: "Viewer",
  expiresAt: "",
};

describe("buildCreateKeyBody", () => {
  it("requires a name", () => {
    expect(buildCreateKeyBody({ ...base, name: "  " })).toEqual({ error: "Name is required." });
  });

  it("drops workspace/workflow/role for an Instance key", () => {
    const body = buildCreateKeyBody({ ...base, scope: "Instance", role: "Owner" });
    expect(body).toMatchObject({ scope: "Instance", workspaceId: null, workflowId: null, role: null });
  });

  it("keeps the workspace and role for a Workspace key", () => {
    const body = buildCreateKeyBody({ ...base, scope: "Workspace", role: "Editor" });
    expect(body).toMatchObject({ scope: "Workspace", workspaceId: "ws-1", workflowId: null, role: "Editor" });
  });

  it("requires a workspace for scoped keys", () => {
    expect(buildCreateKeyBody({ ...base, scope: "Workspace", workspaceId: "" })).toHaveProperty("error");
    expect(buildCreateKeyBody({ ...base, scope: "Workflow", workspaceId: "" })).toHaveProperty("error");
  });

  it("requires a workflow for a Workflow key", () => {
    expect(buildCreateKeyBody({ ...base, scope: "Workflow", workflowId: "" })).toHaveProperty("error");
    const ok = buildCreateKeyBody({ ...base, scope: "Workflow", workflowId: "wf-9" });
    expect(ok).toMatchObject({ scope: "Workflow", workspaceId: "ws-1", workflowId: "wf-9" });
  });

  it("turns a date into an end-of-day UTC ISO expiry, or null when blank", () => {
    expect(buildCreateKeyBody(base)).toMatchObject({ expiresAt: null });
    const body = buildCreateKeyBody({ ...base, expiresAt: "2026-08-01" });
    expect(body).toMatchObject({ expiresAt: "2026-08-01T23:59:59.000Z" });
  });
});

describe("describeKeyTarget", () => {
  it("labels each scope", () => {
    expect(describeKeyTarget("Instance", null, null)).toBe("Instance (all workspaces)");
    expect(describeKeyTarget("Workflow", "Nightly sync", null)).toBe("Workflow · Nightly sync");
    expect(describeKeyTarget("Workspace", null, "Marketing")).toBe("Workspace · Marketing");
  });
});
