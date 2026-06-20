// {{steps.<id>.output…}} reference handling for the builder. <id> is a numeric order or a
// step key. Mirrors the server: keys are slugged from names, unique per version. The builder
// uses this to flag fragile (index-based) and broken refs, and to rewrite indexes → keys.

export type StepLite = { key: string; order: number; name: string | null };

export type StepRefCheck = {
  status: "none" | "ok" | "fragile" | "unknown";
  unknown: string[]; // ids that resolve to no step
  fragile: string[]; // numeric (index-based) ids that resolve but break on reorder
};

const REF_RE = /\{\{\s*steps\.([^.}\s]+)\.output[^}]*\}\}/g;
const INDEX_RE = /(\{\{\s*steps\.)(\d+)(\.output[^}]*\}\})/g;

// Keep in lockstep with StepKey.Slugify on the server.
export function slugifyStepKey(name: string | null | undefined, order: number): string {
  const cleaned = (name ?? "")
    .trim()
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "");
  return cleaned === "" ? `step-${order + 1}` : cleaned;
}

// Mirrors StepKey.AssignAll: slug from key||name, deduped with -2, -3, … in order.
export function assignStepKeys(steps: { name?: string | null; key?: string | null }[]): string[] {
  const taken = new Set<string>();
  return steps.map((step, order) => {
    const base = slugifyStepKey(step.key?.trim() ? step.key : step.name, order);
    if (!taken.has(base)) {
      taken.add(base);
      return base;
    }
    for (let n = 2; ; n++) {
      const candidate = `${base}-${n}`;
      if (!taken.has(candidate)) {
        taken.add(candidate);
        return candidate;
      }
    }
  });
}

export function hasStepRef(value: unknown): boolean {
  return typeof value === "string" && value.includes("{{steps.");
}

export function checkStepRefs(value: unknown, steps: StepLite[]): StepRefCheck {
  if (typeof value !== "string") {
    return { status: "none", unknown: [], fragile: [] };
  }
  const refs = [...value.matchAll(REF_RE)];
  if (refs.length === 0) {
    return { status: "none", unknown: [], fragile: [] };
  }
  const keys = new Set(steps.map((s) => s.key));
  const orders = new Set(steps.map((s) => s.order));
  const unknown: string[] = [];
  const fragile: string[] = [];
  for (const [, id] of refs) {
    if (/^\d+$/.test(id)) {
      if (orders.has(Number(id))) {
        fragile.push(id);
      } else {
        unknown.push(id);
      }
    } else if (!keys.has(id)) {
      unknown.push(id);
    }
  }
  let status: StepRefCheck["status"] = "ok";
  if (unknown.length > 0) {
    status = "unknown";
  } else if (fragile.length > 0) {
    status = "fragile";
  }
  return { status, unknown, fragile };
}

// Rewrites {{steps.<order>.output…}} → {{steps.<key>.output…}} for known orders; leaves
// unknown orders and existing key-refs untouched.
export function rewriteIndexRefs(value: string, steps: StepLite[]): string {
  const byOrder = new Map(steps.map((s) => [s.order, s.key]));
  return value.replace(INDEX_RE, (match, head, num, tail) => {
    const key = byOrder.get(Number(num));
    return key ? `${head}${key}${tail}` : match;
  });
}

// Same rewrite across a whole config object — refs only ever live inside string values, and
// the token braces aren't escaped by JSON.stringify, so a serialize-rewrite-parse round-trip
// is safe and structure-preserving.
export function rewriteConfigIndexRefs(
  config: Record<string, unknown>,
  steps: StepLite[],
): Record<string, unknown> {
  return JSON.parse(rewriteIndexRefs(JSON.stringify(config), steps)) as Record<string, unknown>;
}

// Does any string value in the config carry a numeric (index-based) step ref? Uses a
// non-global regex — INDEX_RE is stateful (/g) and reserved for replace.
export function configHasIndexRef(config: Record<string, unknown>): boolean {
  return /\{\{\s*steps\.\d+\.output[^}]*\}\}/.test(JSON.stringify(config));
}
