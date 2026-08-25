import {
  Background,
  Controls,
  type Edge,
  Handle,
  type Node,
  type NodeProps,
  Position,
  ReactFlow,
} from "@xyflow/react";
import { useMemo } from "react";
import "@xyflow/react/dist/style.css";
import type { KeyEdge } from "./switch-routing";

// status (optional) tints the node for the execution inspector: succeeded/failed/skipped/running.
export type GraphStep = {
  key: number;
  label: string;
  actionType: string;
  status?: string;
};
// entryStepKey = the step this trigger feeds; undefined falls back to the first step.
export type GraphTrigger = {
  key: number;
  label: string;
  entryStepKey?: number;
};
// number = step key; string = a trigger node id ("trigger:<key>").
export type GraphSelection = number | string | null;

const COLUMN = 300;
const ROW = 120;

type StepNodeData = {
  index: number;
  label: string;
  actionType: string;
  selected: boolean;
  unreachable: boolean;
  status?: string;
};

// Execution-status tint (inspector graph). Selection still wins when both apply.
const STATUS_CLASS: Record<string, string> = {
  Succeeded: "border-green-500/70 bg-green-500/10",
  Failed: "border-danger-solid/70 bg-danger-solid/10",
  Skipped: "border-line-strong bg-card opacity-50",
  Running: "border-warning-solid/70 bg-warning-solid/10",
};

function StepNode(props: NodeProps) {
  const data = props.data as StepNodeData;
  const base = data.selected
    ? "border-brand-bright bg-muted"
    : ((data.status && STATUS_CLASS[data.status]) ?? "border-line-strong bg-card");
  return (
    <div
      className={`min-w-44 rounded-[2px] border px-3 py-2 text-left ${base} ${
        data.unreachable ? "border-dashed opacity-50" : ""
      }`}
    >
      <Handle type="target" position={Position.Top} className="!bg-zinc-600" />
      <div className="flex items-center gap-2">
        <span className="text-xs text-muted-foreground">#{data.index + 1}</span>
        <span className="truncate text-sm text-fg-strong">{data.label}</span>
      </div>
      <div className="mt-0.5 truncate text-xs text-muted-foreground">{data.actionType}</div>
      {data.unreachable && (
        <div className="mt-0.5 text-[11px] text-amber-500/80">⚠ not reached — won’t run</div>
      )}
      <Handle type="source" position={Position.Bottom} className="!bg-zinc-600" />
    </div>
  );
}

function TriggerNode(props: NodeProps) {
  const data = props.data as { label?: string; selected?: boolean };
  return (
    <div
      className={`max-w-56 rounded-[2px] border px-3 py-2 text-sm text-violet-200 ${
        data.selected ? "border-violet-400 bg-trigger-solid/20" : "border-trigger-solid/40 bg-trigger-solid/10"
      }`}
    >
      <span className="block truncate">▶ {data.label ?? "Trigger"}</span>
      <Handle type="source" position={Position.Bottom} className="!bg-trigger-solid" />
    </div>
  );
}

const nodeTypes = { step: StepNode, trigger: TriggerNode };

// Tidy top-down tree layout: depth → y, branches fan out horizontally (a parent is centred over
// its children). Linear workflows stay one column; steps unreachable from the entry drop below.
function layoutPositions(steps: GraphStep[], stepEdges: KeyEdge[]) {
  const children = new Map<number, number[]>();
  for (const step of steps) children.set(step.key, []);
  for (const edge of stepEdges) {
    if (children.has(edge.sourceKey) && children.has(edge.targetKey)) {
      children.get(edge.sourceKey)!.push(edge.targetKey);
    }
  }

  const positions = new Map<number, { x: number; y: number }>();
  const visited = new Set<number>();
  let nextColumn = 0;
  let maxDepth = 0;

  const place = (key: number, depth: number): number => {
    if (visited.has(key)) return positions.get(key)!.x;
    visited.add(key);
    maxDepth = Math.max(maxDepth, depth);
    const kids = children.get(key) ?? [];
    const x =
      kids.length === 0
        ? nextColumn++ * COLUMN
        : kids.map((k) => place(k, depth + 1)).reduce((a, b) => a + b, 0) / kids.length;
    positions.set(key, { x, y: (depth + 1) * ROW });
    return x;
  };

  if (steps[0]) place(steps[0].key, 0);

  const unreachable = new Set<number>();
  let orphanColumn = 0;
  for (const step of steps) {
    if (visited.has(step.key)) continue;
    unreachable.add(step.key);
    positions.set(step.key, {
      x: orphanColumn++ * COLUMN,
      y: (maxDepth + 3) * ROW,
    });
  }

  // Siblings whose lanes rejoin at the same step average to the same x and land
  // exactly on top of each other — the parallel-fan-out case draws as one node.
  // Spread any row whose nodes sit closer together than a full column.
  const byRow = new Map<number, number[]>();
  for (const [key, p] of positions) {
    const row = byRow.get(p.y);
    if (row) row.push(key);
    else byRow.set(p.y, [key]);
  }
  for (const keys of byRow.values()) {
    if (keys.length < 2) continue;
    keys.sort((a, b) => positions.get(a)!.x - positions.get(b)!.x);
    for (let i = 1; i < keys.length; i++) {
      const prev = positions.get(keys[i - 1])!;
      const cur = positions.get(keys[i])!;
      if (cur.x - prev.x < COLUMN) cur.x = prev.x + COLUMN;
    }
  }

  return { positions, unreachable };
}

// The workflow as a React Flow graph: trigger nodes feed the first step, steps connect by their
// edges (branches labelled). Used editable by the builder canvas and read-only on the detail page.
export function WorkflowGraph({
  steps,
  triggers,
  stepEdges,
  selection = null,
  onSelect,
  height = "34rem",
}: {
  steps: GraphStep[];
  triggers: GraphTrigger[];
  stepEdges: KeyEdge[];
  selection?: GraphSelection;
  onSelect?: (selection: GraphSelection) => void;
  height?: string;
}) {
  const { positions, unreachable } = useMemo(() => layoutPositions(steps, stepEdges), [steps, stepEdges]);

  const nodes = useMemo<Node[]>(() => {
    const entryX = steps[0] ? (positions.get(steps[0].key)?.x ?? 0) : 0;
    const triggerNodes: Node[] = triggers.map((t, i) => {
      // Sit a trigger over the step it feeds (falls back to the entry column).
      const targetX = t.entryStepKey != null ? positions.get(t.entryStepKey)?.x : undefined;
      return {
        id: `trigger:${t.key}`,
        type: "trigger",
        position: { x: targetX ?? entryX + i * COLUMN, y: 0 },
        data: { label: t.label, selected: selection === `trigger:${t.key}` },
      };
    });
    const stepNodes: Node[] = steps.map((s, i) => ({
      id: String(s.key),
      type: "step",
      position: positions.get(s.key) ?? { x: 0, y: (i + 1) * ROW },
      data: {
        index: i,
        label: s.label,
        actionType: s.actionType,
        selected: s.key === selection,
        unreachable: unreachable.has(s.key),
        status: s.status,
      },
    }));
    return [...triggerNodes, ...stepNodes];
  }, [steps, triggers, selection, positions, unreachable]);

  const edges = useMemo<Edge[]>(() => {
    const list: Edge[] = [];
    const firstId = steps[0] ? String(steps[0].key) : null;
    if (firstId) {
      const stepKeys = new Set(steps.map((s) => s.key));
      for (const t of triggers) {
        // Edge into the step the trigger feeds, or the first step when unset / stale.
        const target =
          t.entryStepKey != null && stepKeys.has(t.entryStepKey) ? String(t.entryStepKey) : firstId;
        list.push({
          id: `e-trigger-${t.key}-${target}`,
          source: `trigger:${t.key}`,
          target,
        });
      }
    }
    for (const edge of stepEdges) {
      const isError = edge.label === "error";
      list.push({
        id: `e-${edge.sourceKey}-${edge.targetKey}-${edge.label ?? ""}`,
        source: String(edge.sourceKey),
        target: String(edge.targetKey),
        label: edge.label ?? undefined,
        labelStyle: { fill: isError ? "#f87171" : "#a1a1aa", fontSize: 11 },
        labelBgStyle: { fill: "#18181b" },
        // The error edge is drawn red/dashed to set the failure path apart from success routing.
        ...(isError ? { style: { stroke: "#f87171", strokeDasharray: "5 4" }, animated: false } : {}),
      });
    }
    return list;
  }, [steps, triggers, stepEdges]);

  // The canvas is a recessed well, not a box: a bordered frame around bordered
  // nodes is a container inside a container, which the design system bans.
  return (
    <div className="overflow-hidden rounded-[2px] bg-rack-void" style={{ height }}>
      <ReactFlow
        nodes={nodes}
        edges={edges}
        nodeTypes={nodeTypes}
        colorMode="dark"
        nodesDraggable={false}
        nodesConnectable={false}
        proOptions={{ hideAttribution: true }}
        onNodeClick={
          onSelect ? (_, node) => onSelect(node.type === "trigger" ? node.id : Number(node.id)) : undefined
        }
        fitView
      >
        <Background />
        <Controls showInteractive={false} />
      </ReactFlow>
    </div>
  );
}
