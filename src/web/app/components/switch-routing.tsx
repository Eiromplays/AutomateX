import type { WorkflowEdgeInput } from "../lib/api";

// Switch routing is authored as "label → target step" (and a default). Targets are stored
// as stable step keys (survive reordering); they're converted to step-order edges on submit.
export type SwitchRouting = {
  byLabel: Record<string, number>;
  default: number | null;
};

export type RoutingStep = {
  key: number;
  actionType: string;
  name: string | null;
  config: Record<string, unknown>;
  routing?: SwitchRouting;
  // Explicit unconditional successors (parallel lanes / joins), authored as stable step keys.
  // When set they replace this step's implicit order backbone.
  fanOut?: number[];
};

export type KeyEdge = {
  sourceKey: number;
  targetKey: number;
  label: string | null;
};

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

// The routes that still correspond to a live case label (plus default). Routes for cases
// that were deleted or renamed are dropped here, so a stale label can't emit a branch edge.
function validRoutes(step: RoutingStep): {
  byLabel: [string, number][];
  default: number | null;
} {
  if (step.actionType !== SWITCH || !step.routing) return { byLabel: [], default: null };
  const labels = new Set(caseLabelsOf(step.config));
  return {
    byLabel: Object.entries(step.routing.byLabel).filter(([label]) => labels.has(label)),
    default: step.routing.default,
  };
}

function isConfiguredSwitch(step: RoutingStep): boolean {
  const routes = validRoutes(step);
  return routes.byLabel.length > 0 || routes.default != null;
}

// A step's live fan-out targets — self-references and keys of deleted steps are dropped.
export function validFanOut(step: RoutingStep, liveKeys: Set<number>): number[] {
  return (step.fanOut ?? []).filter((key) => key !== step.key && liveKeys.has(key));
}

function liveKeySet(steps: RoutingStep[]): Set<number> {
  return new Set(steps.map((s) => s.key));
}

// A workflow is "branched" once any switch has a target wired or any step fans out. Until then it
// stays linear and emits no edges (backward compatible — the engine runs it by order).
export function isBranched(steps: RoutingStep[]): boolean {
  const live = liveKeySet(steps);
  return steps.some((s) => isConfiguredSwitch(s) || validFanOut(s, live).length > 0);
}

// The logical edges as step keys, for canvas display. Linear workflows show their implicit
// order backbone; branched ones route each switch to its targets and chain the rest by order,
// cutting the backbone into a lane head so lanes stay separate.
export function keyEdges(steps: RoutingStep[]): KeyEdge[] {
  if (!isBranched(steps)) {
    const edges: KeyEdge[] = [];
    for (let i = 0; i < steps.length - 1; i++) {
      edges.push({
        sourceKey: steps[i].key,
        targetKey: steps[i + 1].key,
        label: null,
      });
    }
    return edges;
  }

  const live = liveKeySet(steps);
  // Switch route targets are lane heads — the implicit order backbone is cut before them so a
  // neighbouring lane doesn't bleed in. Fan-out targets are explicit joins/lanes and are terminal
  // by default (they emit only what they themselves fan out to).
  const switchTargets = new Set<number>();
  const fanOutTargets = new Set<number>();
  for (const step of steps) {
    const routes = validRoutes(step);
    for (const [, key] of routes.byLabel) switchTargets.add(key);
    if (routes.default != null) switchTargets.add(routes.default);
    for (const key of validFanOut(step, live)) fanOutTargets.add(key);
  }

  const edges: KeyEdge[] = [];
  for (let i = 0; i < steps.length; i++) {
    const step = steps[i];
    const fanOut = validFanOut(step, live);
    if (isConfiguredSwitch(step)) {
      const routes = validRoutes(step);
      for (const [label, key] of routes.byLabel) {
        edges.push({ sourceKey: step.key, targetKey: key, label });
      }
      if (routes.default != null) {
        edges.push({
          sourceKey: step.key,
          targetKey: routes.default,
          label: "default",
        });
      }
    } else if (fanOut.length > 0) {
      for (const key of fanOut) edges.push({ sourceKey: step.key, targetKey: key, label: null });
    } else if (!fanOutTargets.has(step.key)) {
      // No explicit successors and not an explicit lane head — chain to the next step by order,
      // unless that next step is a switch lane head.
      const next = steps[i + 1];
      if (next && !switchTargets.has(next.key)) {
        edges.push({ sourceKey: step.key, targetKey: next.key, label: null });
      }
    }
  }
  return edges;
}

// The implicit order backbone (i → i+1). The engine runs an edgeless workflow this way, so the
// read-only detail graph draws it when no branch edges were persisted.
export function backboneEdges(stepKeys: number[]): KeyEdge[] {
  const edges: KeyEdge[] = [];
  for (let i = 0; i < stepKeys.length - 1; i++) {
    edges.push({
      sourceKey: stepKeys[i],
      targetKey: stepKeys[i + 1],
      label: null,
    });
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

// Rebuild authored intent from persisted edges (the reverse of submitEdges): labelled edges
// become switch routes, unlabelled edges become explicit fan-out. Reconstructing every
// unlabelled edge as fan-out round-trips exactly — terminal lanes simply have no edge.
export function routingFromEdges(steps: RoutingStep[], edges: WorkflowEdgeInput[]): void {
  for (const edge of edges) {
    const from = steps[edge.from];
    const toKey = steps[edge.to]?.key;
    if (!from || toKey == null) continue;
    if (edge.label != null) {
      from.routing ??= { byLabel: {}, default: null };
      if (edge.label === "default") from.routing.default = toKey;
      else from.routing.byLabel[edge.label] = toKey;
    } else {
      from.fanOut ??= [];
      if (!from.fanOut.includes(toKey)) from.fanOut.push(toKey);
    }
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
    const next: SwitchRouting = {
      byLabel: { ...routing.byLabel },
      default: routing.default,
    };
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
    ...labels.map((l) => ({
      key: l,
      label: l,
      value: routing.byLabel[l] ?? null,
    })),
    { key: "default", label: "default", value: routing.default },
  ];

  return (
    <div className="space-y-2 rounded-md border border-zinc-800 bg-zinc-900/40 p-2">
      <p className="text-xs font-medium text-zinc-400">Routes</p>
      {labels.length === 0 && (
        <p className="text-[11px] text-zinc-600">
          Add a case with a label above, then point it at a step here.
        </p>
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

// Unconditional successor picker for a non-switch step. Selecting more than one target fans the
// run out into concurrent lanes; pointing several steps at the same target makes it a join.
export function FanOutTargets({
  step,
  steps,
  onChange,
}: {
  step: RoutingStep;
  steps: RoutingStep[];
  onChange: (fanOut: number[]) => void;
}) {
  const others = steps.filter((s) => s.key !== step.key);
  const selected = new Set(validFanOut(step, liveKeySet(steps)));
  const indexOf = (key: number) => steps.findIndex((s) => s.key === key);
  const optionLabel = (s: RoutingStep) => `#${indexOf(s.key) + 1} ${s.name || s.actionType}`;

  const toggle = (key: number, on: boolean) => {
    const next = new Set(selected);
    if (on) next.add(key);
    else next.delete(key);
    onChange([...next]);
  };

  return (
    <div className="space-y-2 rounded-md border border-zinc-800 bg-zinc-900/40 p-2">
      <p className="text-xs font-medium text-zinc-400">Parallel branches</p>
      {others.length === 0 && <p className="text-[11px] text-zinc-600">Add another step to branch into.</p>}
      {others.map((s) => (
        <label key={s.key} className="flex items-center gap-2 text-xs text-zinc-300">
          <input
            type="checkbox"
            className="accent-emerald-500"
            checked={selected.has(s.key)}
            onChange={(e) => toggle(s.key, e.target.checked)}
          />
          {optionLabel(s)}
        </label>
      ))}
      <p className="text-[11px] text-zinc-600">
        Pick none to run the next step in order. Pick two or more to run them at once; point several steps at
        the same one to join.
      </p>
    </div>
  );
}
