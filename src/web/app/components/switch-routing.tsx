import type { WorkflowEdgeInput } from "../lib/api";

// Switch routing is authored as "label → target step" (and a default). Targets are stored
// as stable step keys (survive reordering); they're converted to step-order edges on submit.
export type SwitchRouting = { byLabel: Record<string, number>; default: number | null };

export type RoutingStep = {
  key: number;
  actionType: string;
  name: string | null;
  config: Record<string, unknown>;
  routing?: SwitchRouting;
};

export type KeyEdge = { sourceKey: number; targetKey: number; label: string | null };

const SWITCH = "switch";
const inputClass =
  "w-full rounded-md border border-zinc-700 bg-zinc-900 px-3 py-1.5 text-sm " +
  "placeholder:text-zinc-600 focus:border-emerald-500 focus:outline-none";

// Distinct, non-empty case labels from a switch step's config — these are the routable paths.
export function caseLabelsOf(config: Record<string, unknown>): string[] {
  const cases = Array.isArray(config.cases) ? config.cases : [];
  const labels = cases
    .map((c) => (c && typeof c === "object" ? String((c as { label?: unknown }).label ?? "") : ""))
    .filter((l) => l.length > 0);
  return [...new Set(labels)];
}

function isConfiguredSwitch(step: RoutingStep): boolean {
  return (
    step.actionType === SWITCH &&
    !!step.routing &&
    (Object.keys(step.routing.byLabel).length > 0 || step.routing.default != null)
  );
}

// A workflow is "branched" once any switch has a target wired. Until then it stays linear
// and emits no edges (backward compatible — the engine runs it by order).
export function isBranched(steps: RoutingStep[]): boolean {
  return steps.some(isConfiguredSwitch);
}

// The logical edges as step keys, for canvas display. Linear workflows show their implicit
// order backbone; branched ones route each switch to its targets and chain the rest by order,
// cutting the backbone into a lane head so lanes stay separate.
export function keyEdges(steps: RoutingStep[]): KeyEdge[] {
  if (!isBranched(steps)) {
    const edges: KeyEdge[] = [];
    for (let i = 0; i < steps.length - 1; i++) {
      edges.push({ sourceKey: steps[i].key, targetKey: steps[i + 1].key, label: null });
    }
    return edges;
  }

  const targetKeys = new Set<number>();
  for (const step of steps) {
    if (!isConfiguredSwitch(step)) continue;
    for (const key of Object.values(step.routing!.byLabel)) targetKeys.add(key);
    if (step.routing!.default != null) targetKeys.add(step.routing!.default);
  }

  const edges: KeyEdge[] = [];
  for (let i = 0; i < steps.length; i++) {
    const step = steps[i];
    if (isConfiguredSwitch(step)) {
      for (const [label, key] of Object.entries(step.routing!.byLabel)) {
        edges.push({ sourceKey: step.key, targetKey: key, label });
      }
      if (step.routing!.default != null) {
        edges.push({ sourceKey: step.key, targetKey: step.routing!.default, label: "default" });
      }
    } else {
      const next = steps[i + 1];
      if (next && !targetKeys.has(next.key)) {
        edges.push({ sourceKey: step.key, targetKey: next.key, label: null });
      }
    }
  }
  return edges;
}

// Edges to submit: order-based, or empty when the workflow is still linear.
export function submitEdges(steps: RoutingStep[]): WorkflowEdgeInput[] {
  if (!isBranched(steps)) return [];
  const keyToIndex = new Map(steps.map((s, i) => [s.key, i]));
  return keyEdges(steps).flatMap((edge) => {
    const from = keyToIndex.get(edge.sourceKey);
    const to = keyToIndex.get(edge.targetKey);
    return from == null || to == null ? [] : [{ from, to, label: edge.label }];
  });
}

// Rebuild per-switch routing from persisted edges (the reverse of submitEdges). Backbone
// (unlabelled) edges are ignored — they're recomputed from order.
export function routingFromEdges(steps: RoutingStep[], edges: WorkflowEdgeInput[]): void {
  for (const edge of edges) {
    if (edge.label == null) continue;
    const from = steps[edge.from];
    const toKey = steps[edge.to]?.key;
    if (!from || toKey == null) continue;
    from.routing ??= { byLabel: {}, default: null };
    if (edge.label === "default") from.routing.default = toKey;
    else from.routing.byLabel[edge.label] = toKey;
  }
}

// "label → target step" selectors for a switch step. Lives next to the case editor in
// whichever builder mode is active.
export function SwitchTargets({
  step,
  steps,
  onChange,
}: {
  step: RoutingStep;
  steps: RoutingStep[];
  onChange: (routing: SwitchRouting) => void;
}) {
  const labels = caseLabelsOf(step.config);
  const routing = step.routing ?? { byLabel: {}, default: null };
  const others = steps.filter((s) => s.key !== step.key);
  const indexOf = (key: number) => steps.findIndex((s) => s.key === key);
  const optionLabel = (s: RoutingStep) => `#${indexOf(s.key) + 1} ${s.name || s.actionType}`;

  const setTarget = (key: string | "default", targetKey: number | null) => {
    const next: SwitchRouting = { byLabel: { ...routing.byLabel }, default: routing.default };
    if (key === "default") {
      next.default = targetKey;
    } else if (targetKey == null) {
      delete next.byLabel[key];
    } else {
      next.byLabel[key] = targetKey;
    }
    onChange(next);
  };

  const rows: { key: string; label: string; value: number | null }[] = [
    ...labels.map((l) => ({ key: l, label: l, value: routing.byLabel[l] ?? null })),
    { key: "default", label: "default", value: routing.default },
  ];

  return (
    <div className="space-y-2 rounded-md border border-zinc-800 bg-zinc-900/40 p-2">
      <p className="text-xs font-medium text-zinc-400">Routes</p>
      {labels.length === 0 && (
        <p className="text-[11px] text-zinc-600">Add a case with a label above, then point it at a step here.</p>
      )}
      {rows.map((row) => (
        <div key={row.key} className="flex items-center gap-2">
          <span className="w-16 shrink-0 truncate text-xs text-zinc-300" title={row.label}>
            {row.label}
          </span>
          <span className="text-zinc-600">→</span>
          <select
            className={inputClass}
            value={row.value ?? ""}
            onChange={(e) => setTarget(row.key, e.target.value === "" ? null : Number(e.target.value))}
          >
            <option value="">— end —</option>
            {others.map((s) => (
              <option key={s.key} value={s.key}>
                {optionLabel(s)}
              </option>
            ))}
          </select>
        </div>
      ))}
      <p className="text-[11px] text-zinc-600">
        Each label runs from its step up to the next branch target (or end). “— end —” stops that path.
      </p>
    </div>
  );
}
