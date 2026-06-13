import { useMemo, useState } from "react";
import { ReactFlow, Background, Controls, Handle, Position, type Node, type Edge, type NodeProps } from "@xyflow/react";
import "@xyflow/react/dist/style.css";
import type { ActionDescriptor, CreateWorkflowStep } from "../lib/api";
import { SchemaForm, type JsonSchema } from "./schema-form";
import { groupBySource, sourceKind, sourceLabel } from "./action-source";
import { SwitchTargets, type KeyEdge, type SwitchRouting } from "./switch-routing";
import { newDraftTrigger, triggerSummary, TriggerEditor, type DraftTrigger } from "./workflow-triggers";

type DraftStep = CreateWorkflowStep & { key: number; routing?: SwitchRouting };

type StepNodeData = { index: number; label: string; actionType: string; selected: boolean; unreachable?: boolean };

// number = step key; string = a trigger node id ("trigger:<key>").
type Selection = number | string | null;

const fieldClass =
  "w-full rounded-md border border-zinc-700 bg-zinc-900 px-3 py-1.5 text-sm " +
  "placeholder:text-zinc-600 focus:border-emerald-500 focus:outline-none";

function StepNode(props: NodeProps) {
  const data = props.data as StepNodeData;
  return (
    <div
      className={`min-w-44 rounded-lg border px-3 py-2 text-left ${
        data.selected ? "border-emerald-500 bg-zinc-800" : "border-zinc-700 bg-zinc-900"
      } ${data.unreachable ? "border-dashed opacity-50" : ""}`}
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

const COLUMN = 240;
const ROW = 110;

// Tidy top-down tree layout: depth → y, and branches fan out horizontally (a parent is
// centred over its children). Keeps a linear workflow in one column and forks side by side.
// Steps unreachable from the entry (e.g. a branch points elsewhere) drop to a row below the
// tree and are flagged so the UI can dim them — they won't run.
type Layout = { positions: Map<number, { x: number; y: number }>; unreachable: Set<number> };

function layoutPositions(steps: DraftStep[], stepEdges: KeyEdge[]): Layout {
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

  // The engine always enters at the first step; everything else hangs off the edge graph.
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

export function WorkflowCanvas({
  steps,
  stepEdges,
  actions,
  schemaFor,
  onUpdateStep,
  onMoveStep,
  onAddStep,
  onRemoveStep,
  triggers,
  onTriggersChange,
}: {
  steps: DraftStep[];
  stepEdges: KeyEdge[];
  actions: ActionDescriptor[];
  schemaFor: (actionType: string) => JsonSchema | null;
  onUpdateStep: (key: number, patch: Partial<DraftStep>) => void;
  onMoveStep: (index: number, delta: number) => void;
  onAddStep: () => void;
  onRemoveStep: (key: number) => void;
  triggers: DraftTrigger[];
  onTriggersChange: (triggers: DraftTrigger[]) => void;
}) {
  const [selection, setSelection] = useState<Selection>(steps[0]?.key ?? null);

  const displayName = (actionType: string) => actions.find((a) => a.type === actionType)?.displayName ?? actionType;

  // Trigger nodes are the editable drafts; selecting one edits it in the side panel.
  const triggerNodeList = useMemo(
    () => triggers.map((t) => ({ id: `trigger:${t.key}`, label: triggerSummary(t) })),
    [triggers],
  );

  const addTrigger = () => {
    const draft = newDraftTrigger();
    onTriggersChange([...triggers, draft]);
    setSelection(`trigger:${draft.key}`);
  };

  const { positions, unreachable } = useMemo(() => layoutPositions(steps, stepEdges), [steps, stepEdges]);

  const nodes = useMemo<Node[]>(() => {
    const entryX = steps[0] ? positions.get(steps[0].key)?.x ?? 0 : 0;
    const triggerNodes: Node[] = triggerNodeList.map((t, i) => ({
      id: t.id,
      type: "trigger",
      position: { x: entryX + i * COLUMN, y: 0 },
      data: { label: t.label, selected: selection === t.id },
    }));
    const stepNodes: Node[] = steps.map((s, i) => ({
      id: String(s.key),
      type: "step",
      position: positions.get(s.key) ?? { x: 0, y: (i + 1) * ROW },
      data: {
        index: i,
        label: s.name || displayName(s.actionType),
        actionType: s.actionType,
        selected: s.key === selection,
        unreachable: unreachable.has(s.key),
      },
    }));
    return [...triggerNodes, ...stepNodes];
  }, [steps, selection, actions, triggerNodeList, positions, unreachable]);

  const edges = useMemo<Edge[]>(() => {
    const list: Edge[] = [];
    const firstId = steps[0] ? String(steps[0].key) : null;
    if (firstId) {
      for (const t of triggerNodeList) {
        list.push({ id: `e-${t.id}-${firstId}`, source: t.id, target: firstId });
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
  }, [steps, triggerNodeList, stepEdges]);

  const selectedStep = typeof selection === "number" ? steps.find((s) => s.key === selection) ?? null : null;
  const selectedIndex = selectedStep ? steps.findIndex((s) => s.key === selectedStep.key) : -1;
  const selectedTriggerKey =
    typeof selection === "string" && selection.startsWith("trigger:")
      ? Number(selection.slice("trigger:".length))
      : null;
  const selectedTrigger = selectedTriggerKey != null ? triggers.find((t) => t.key === selectedTriggerKey) ?? null : null;

  return (
    <div className="grid gap-3 lg:grid-cols-[1fr_22rem]">
      <div className="h-[34rem] overflow-hidden rounded-lg border border-zinc-800 bg-zinc-950">
        <ReactFlow
          nodes={nodes}
          edges={edges}
          nodeTypes={nodeTypes}
          colorMode="dark"
          nodesDraggable={false}
          nodesConnectable={false}
          proOptions={{ hideAttribution: true }}
          onNodeClick={(_, node) => setSelection(node.type === "trigger" ? node.id : Number(node.id))}
          fitView
        >
          <Background />
          <Controls showInteractive={false} />
        </ReactFlow>
      </div>

      <div className="space-y-3 rounded-lg border border-zinc-800 p-4">
        <div className="flex gap-2">
          <button
            type="button"
            onClick={onAddStep}
            className="flex-1 rounded-md border border-zinc-700 px-2.5 py-1 text-xs hover:bg-zinc-900"
          >
            + Add step
          </button>
          <button
            type="button"
            onClick={addTrigger}
            className="flex-1 rounded-md border border-violet-500/40 px-2.5 py-1 text-xs text-violet-200 hover:bg-violet-500/10"
          >
            + Add trigger
          </button>
        </div>

        {selectedTrigger ? (
          <div className="space-y-2">
            <div className="text-xs font-medium text-violet-300">Trigger</div>
            <TriggerEditor
              draft={selectedTrigger}
              onChange={(draft) => onTriggersChange(triggers.map((t) => (t.key === draft.key ? draft : t)))}
              onRemove={() => {
                onTriggersChange(triggers.filter((t) => t.key !== selectedTrigger.key));
                setSelection(null);
              }}
            />
            <p className="text-[11px] text-zinc-600">Fires this workflow. Changes apply when you save.</p>
          </div>
        ) : selectedStep ? (
          <div className="space-y-3">
            <div className="flex items-center justify-between text-xs text-zinc-500">
              <span>Step #{selectedIndex + 1}</span>
              <span className="flex gap-1">
                <button type="button" onClick={() => onMoveStep(selectedIndex, -1)} className="px-1 hover:text-zinc-200">↑</button>
                <button type="button" onClick={() => onMoveStep(selectedIndex, 1)} className="px-1 hover:text-zinc-200">↓</button>
                <button
                  type="button"
                  onClick={() => {
                    onRemoveStep(selectedStep.key);
                    setSelection(null);
                  }}
                  className="px-1 hover:text-red-400"
                >
                  ✕
                </button>
              </span>
            </div>
            <select
              className={fieldClass}
              value={selectedStep.actionType}
              onChange={(e) =>
                onUpdateStep(selectedStep.key, { actionType: e.target.value, config: {}, routing: undefined })
              }
            >
              {groupBySource(actions).map(([source, items]) => (
                <optgroup
                  key={source}
                  label={
                    sourceKind(source) === "workspace"
                      ? `${sourceLabel(source)} — workspace override`
                      : sourceLabel(source)
                  }
                >
                  {items.map((action) => (
                    <option key={action.type} value={action.type}>
                      {action.displayName} ({action.type})
                    </option>
                  ))}
                </optgroup>
              ))}
            </select>
            <input
              className={fieldClass}
              placeholder="Step name (optional)"
              value={selectedStep.name ?? ""}
              onChange={(e) => onUpdateStep(selectedStep.key, { name: e.target.value || null })}
            />
            <SchemaForm
              schema={schemaFor(selectedStep.actionType)}
              value={selectedStep.config}
              actionType={selectedStep.actionType}
              onChange={(config) => onUpdateStep(selectedStep.key, { config })}
            />
            {selectedStep.actionType === "switch" && (
              <SwitchTargets
                step={selectedStep}
                steps={steps}
                onChange={(routing) => onUpdateStep(selectedStep.key, { routing })}
              />
            )}
          </div>
        ) : (
          <p className="text-xs text-zinc-600">Select a step to edit, a trigger node for info, or add a step.</p>
        )}
      </div>
    </div>
  );
}
