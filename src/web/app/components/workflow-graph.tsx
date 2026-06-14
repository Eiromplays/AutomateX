import { useMemo } from "react";
import { ReactFlow, Background, Controls, Handle, Position, type Node, type Edge, type NodeProps } from "@xyflow/react";
import "@xyflow/react/dist/style.css";
import { type KeyEdge } from "./switch-routing";

// status (optional) tints the node for the execution inspector: succeeded/failed/skipped/running.
export type GraphStep = { key: number; label: string; actionType: string; status?: string };
// entryStepKey = the step this trigger feeds; undefined falls back to the first step.
export type GraphTrigger = { key: number; label: string; entryStepKey?: number };
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
  Failed: "border-red-500/70 bg-red-500/10",
  Skipped: "border-zinc-700 bg-zinc-900 opacity-50",
  Running: "border-amber-500/70 bg-amber-500/10",
};

function StepNode(props: NodeProps) {
  const data = props.data as StepNodeData;
  const base = data.selected
    ? "border-emerald-500 bg-zinc-800"
    : (data.status && STATUS_CLASS[data.status]) ?? "border-zinc-700 bg-zinc-900";
  return (
    <div
      className={`min-w-44 rounded-lg border px-3 py-2 text-left ${base} ${
        data.unreachable ? "border-dashed opacity-50" : ""
      }`}
    >
      <Handle type="target" position={Position.Top} className="!bg-zinc-600" />
      <div className="flex items-center gap-2">
        <span className="text-xs text-zinc-500">#{data.index + 1}</span>
        <span className="truncate text-sm text-zinc-100">{data.label}</span>
      </div>
      <div className="mt-0.5 truncate text-xs text-zinc-500">{data.actionType}</div>
      {data.unreachable && <div className="mt-0.5 text-[10px] text-amber-500/80">⚠ not reached — won’t run</div>}
      <Handle type="source" position={Position.Bottom} className="!bg-zinc-600" />
    </div>
  );
}

function TriggerNode(props: NodeProps) {
  const data = props.data as { label?: string; selected?: boolean };
  return (
    <div
      className={`max-w-56 rounded-lg border px-3 py-2 text-sm text-violet-200 ${
        data.selected ? "border-violet-400 bg-violet-500/20" : "border-violet-500/40 bg-violet-500/10"
      }`}
    >
      <span className="block truncate">▶ {data.label ?? "Trigger"}</span>
      <Handle type="source" position={Position.Bottom} className="!bg-violet-500" />
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
    const x = kids.length === 0
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
    positions.set(step.key, { x: orphanColumn++ * COLUMN, y: (maxDepth + 3) * ROW });
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
    const entryX = steps[0] ? positions.get(steps[0].key)?.x ?? 0 : 0;
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
        const target = t.entryStepKey != null && stepKeys.has(t.entryStepKey) ? String(t.entryStepKey) : firstId;
        list.push({ id: `e-trigger-${t.key}-${target}`, source: `trigger:${t.key}`, target });
      }
    }
    for (const edge of stepEdges) {
      list.push({
        id: `e-${edge.sourceKey}-${edge.targetKey}-${edge.label ?? ""}`,
        source: String(edge.sourceKey),
        target: String(edge.targetKey),
        label: edge.label ?? undefined,
        labelStyle: { fill: "#a1a1aa", fontSize: 11 },
        labelBgStyle: { fill: "#18181b" },
      });
    }
    return list;
  }, [steps, triggers, stepEdges]);

  return (
    <div className="overflow-hidden rounded-lg border border-zinc-800 bg-zinc-950" style={{ height }}>
      <ReactFlow
        nodes={nodes}
        edges={edges}
        nodeTypes={nodeTypes}
        colorMode="dark"
        nodesDraggable={false}
        nodesConnectable={false}
        proOptions={{ hideAttribution: true }}
        onNodeClick={onSelect ? (_, node) => onSelect(node.type === "trigger" ? node.id : Number(node.id)) : undefined}
        fitView
      >
        <Background />
        <Controls showInteractive={false} />
      </ReactFlow>
    </div>
  );
}
