// Resolves {{connections.<name>.<field>}} references found in a step-config value against the
// known connections, so the builder can flag typos / missing keys before runtime. Connection
// refs are always exactly name.field (templating goes deeper for steps/trigger, not connections).

export type ConnectionLite = { name: string; secretKeys: string[] };

export type RefCheck = {
  status: "none" | "ok" | "unknown";
  unknown: string[]; // "<name>.<field>" entries that don't resolve
};

const REF_RE = /\{\{connections\.([^.}\s]+)\.([^.}\s]+)\}\}/g;

export function hasConnectionRef(value: unknown): boolean {
  return typeof value === "string" && value.includes("{{connections.");
}

export function checkConnectionRefs(value: unknown, connections: ConnectionLite[]): RefCheck {
  if (typeof value !== "string") {
    return { status: "none", unknown: [] };
  }
  const refs = [...value.matchAll(REF_RE)];
  if (refs.length === 0) {
    return { status: "none", unknown: [] };
  }
  const unknown: string[] = [];
  for (const [, name, field] of refs) {
    const conn = connections.find((c) => c.name === name);
    if (!conn || !conn.secretKeys.includes(field)) {
      unknown.push(`${name}.${field}`);
    }
  }
  return { status: unknown.length > 0 ? "unknown" : "ok", unknown };
}
