import type { ApiKeyScope, CreateApiKeyBody } from "./api";

export type KeyFormState = {
  name: string;
  scope: ApiKeyScope;
  workspaceId: string; // the current workspace (targets Workspace/Workflow keys)
  workflowId: string; // required only for a Workflow-scoped key
  role: string; // ignored for Instance scope (server pins it to Owner)
  expiresAt: string; // <input type="date"> value; "" = never
  rateLimitPerMinute: string; // number input; "" = unlimited
  ipAllowlist: string; // free text, one CIDR/IP per line or comma-separated; "" = unrestricted
};

// Split the allowlist textarea into trimmed, non-empty entries.
export function parseIpAllowlist(raw: string): string[] {
  return raw
    .split(/[\n,]/)
    .map((entry) => entry.trim())
    .filter((entry) => entry.length > 0);
}

// A best-effort IP/CIDR shape check that mirrors the server's kinds of validation (the server is
// authoritative and re-validates strictly, so this just catches obvious typos before the round-trip).
export function isValidIpOrCidr(entry: string): boolean {
  const parts = entry.split("/");
  if (parts.length > 2) {
    return false;
  }

  const [address, prefix] = parts;
  const v4 = isIpv4(address);
  const v6 = !v4 && isIpv6(address);
  if (!v4 && !v6) {
    return false;
  }

  if (prefix === undefined) {
    return true;
  }

  if (!/^\d{1,3}$/.test(prefix)) {
    return false;
  }

  const bits = Number(prefix);
  return v4 ? bits <= 32 : bits <= 128;
}

function isIpv4(value: string): boolean {
  const match = /^(\d{1,3})\.(\d{1,3})\.(\d{1,3})\.(\d{1,3})$/.exec(value);
  return match !== null && match.slice(1).every((octet) => Number(octet) <= 255);
}

function isIpv6(value: string): boolean {
  return value.includes(":") && /^[0-9a-fA-F:]+$/.test(value);
}

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

  const rateLimitRaw = form.rateLimitPerMinute.trim();
  let rateLimitPerMinute: number | null = null;
  if (rateLimitRaw) {
    const parsed = Number(rateLimitRaw);
    if (!Number.isInteger(parsed) || parsed < 1) {
      return { error: "Rate limit must be a positive whole number." };
    }
    rateLimitPerMinute = parsed;
  }

  const ipAllowlist = parseIpAllowlist(form.ipAllowlist);
  const invalid = ipAllowlist.filter((entry) => !isValidIpOrCidr(entry));
  if (invalid.length > 0) {
    return { error: `Not a valid IP or CIDR: ${invalid.join(", ")}` };
  }

  return {
    name,
    scope: form.scope,
    workspaceId: form.scope === "Instance" ? null : form.workspaceId,
    workflowId: form.scope === "Workflow" ? form.workflowId : null,
    role: form.scope === "Instance" ? null : form.role,
    // A bare date means "expires at the end of that day, UTC"; empty means never.
    expiresAt: form.expiresAt ? new Date(`${form.expiresAt}T23:59:59Z`).toISOString() : null,
    rateLimitPerMinute,
    ipAllowlist,
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
