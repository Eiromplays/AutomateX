import { describe, expect, it } from "vitest";
import {
  buildCreateKeyBody,
  describeKeyTarget,
  isValidIpOrCidr,
  type KeyFormState,
  parseIpAllowlist,
} from "./api-keys-form";

const base: KeyFormState = {
  name: "grafana",
  scope: "Workspace",
  workspaceId: "ws-1",
  workflowId: "",
  role: "Viewer",
  expiresAt: "",
  rateLimitPerMinute: "",
  ipAllowlist: "",
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

  it("defaults the guards to unlimited + open", () => {
    expect(buildCreateKeyBody(base)).toMatchObject({ rateLimitPerMinute: null, ipAllowlist: [] });
  });

  it("parses a positive rate limit and rejects non-positive or fractional", () => {
    expect(buildCreateKeyBody({ ...base, rateLimitPerMinute: "120" })).toMatchObject({ rateLimitPerMinute: 120 });
    expect(buildCreateKeyBody({ ...base, rateLimitPerMinute: "0" })).toHaveProperty("error");
    expect(buildCreateKeyBody({ ...base, rateLimitPerMinute: "-5" })).toHaveProperty("error");
    expect(buildCreateKeyBody({ ...base, rateLimitPerMinute: "1.5" })).toHaveProperty("error");
  });

  it("parses the allowlist and rejects malformed entries", () => {
    expect(buildCreateKeyBody({ ...base, ipAllowlist: "203.0.113.0/24\n198.51.100.7" })).toMatchObject({
      ipAllowlist: ["203.0.113.0/24", "198.51.100.7"],
    });
    expect(buildCreateKeyBody({ ...base, ipAllowlist: "203.0.113.0/24, nope" })).toHaveProperty("error");
  });
});

describe("ip allowlist helpers", () => {
  it("splits on newlines and commas, trimming blanks", () => {
    expect(parseIpAllowlist(" 10.0.0.0/8 ,\n 198.51.100.7 \n\n")).toEqual(["10.0.0.0/8", "198.51.100.7"]);
  });

  it("accepts IPs and CIDRs, rejects junk and out-of-range prefixes", () => {
    expect(isValidIpOrCidr("203.0.113.7")).toBe(true);
    expect(isValidIpOrCidr("203.0.113.0/24")).toBe(true);
    expect(isValidIpOrCidr("::1")).toBe(true);
    expect(isValidIpOrCidr("fd00::/8")).toBe(true);
    expect(isValidIpOrCidr("not-an-ip")).toBe(false);
    expect(isValidIpOrCidr("203.0.113.7/40")).toBe(false);
    expect(isValidIpOrCidr("999.0.0.1")).toBe(false);
  });
});

describe("describeKeyTarget", () => {
  it("labels each scope", () => {
    expect(describeKeyTarget("Instance", null, null)).toBe("Instance (all workspaces)");
    expect(describeKeyTarget("Workflow", "Nightly sync", null)).toBe("Workflow · Nightly sync");
    expect(describeKeyTarget("Workspace", null, "Marketing")).toBe("Workspace · Marketing");
  });
});
