import { useMemo, useState } from "react";
import { ReactFlow, Background, Controls, Handle, Position, type Node, type Edge, type NodeProps } from "@xyflow/react";
import "@xyflow/react/dist/style.css";
import type { ActionDescriptor, CreateWorkflowStep } from "../lib/api";
import { SchemaForm, type JsonSchema } from "./schema-form";
import { groupBySource, sourceKind, sourceLabel } from "./action-source";

type DraftStep = CreateWorkflowStep & { key: number };

type StepNodeData = { index: number; label: string; actionType: string; selected: boolean };

type Selection = number | "trigger" | null;

const fieldClass =
  "w-full rounded-md border border-zinc-700 bg-zinc-900 px-3 py-1.5 text-sm " +
  "placeholder:text-zinc-600 focus:border-emerald-500 focus:outline-none";

function StepNode(props: NodeProps) {
  const data = props.data as StepNodeData;
  return (
    <div
      className={`min-w-44 rounded-lg border px-3 py-2 text-left ${
        data.selected ? "border-emerald-500 bg-zinc-800" : "border-zinc-700 bg-zinc-900"
      }`}
    >
      <Handle type="target" position={Position.Top} className="!bg-zinc-600" />
      <div className="flex items-center gap-2">
        <span className="text-xs text-zinc-500">#{data.index + 1}</span>
        <span className="truncate text-sm text-zinc-100">{data.label}</span>
      </div>
      <div className="mt-0.5 truncate text-xs text-zinc-500">{data.actionType}</div>
      <Handle type="source" position={Position.Bottom} className="!bg-zinc-600" />
    </div>
  );
}

function TriggerNode(props: NodeProps) {
  const data = props.data as { selected?: boolean };
  return (
    <div
      className={`rounded-lg border px-3 py-2 text-sm text-violet-200 ${
        data.selected ? "border-violet-400 bg-violet-500/20" : "border-violet-500/40 bg-violet-500/10"
      }`}
    >
      ▶ Trigger
      <Handle type="source" position={Position.Bottom} className="!bg-violet-500" />
    </div>
  );
}

const nodeTypes = { step: StepNode, trigger: TriggerNode };

export function WorkflowCanvas({
  steps,
  actions,
  schemaFor,
  onUpdateStep,
  onMoveStep,
  onAddStep,
  onRemoveStep,
}: {
  steps: DraftStep[];
  actions: ActionDescriptor[];
  schemaFor: (actionType: string) => JsonSchema | null;
  onUpdateStep: (key: number, patch: Partial<DraftStep>) => void;
  onMoveStep: (index: number, delta: number) => void;
  onAddStep: () => void;
  onRemoveStep: (key: number) => void;
}) {
  const [selection, setSelection] = useState<Selection>(steps[0]?.key ?? null);

  const displayName = (actionType: string) => actions.find((a) => a.type === actionType)?.displayName ?? actionType;

  const nodes = useMemo<Node[]>(() => {
    const trigger: Node = { id: "trigger", type: "trigger", position: { x: 40, y: 0 }, data: { selected: selection === "trigger" } };
    const stepNodes: Node[] = steps.map((s, i) => ({
      id: String(s.key),
      type: "step",
      position: { x: 0, y: 90 * (i + 1) },
      data: { index: i, label: s.name || displayName(s.actionType), actionType: s.actionType, selected: s.key === selection },
    }));
    return [trigger, ...stepNodes];
  }, [steps, selection, actions]);

  const edges = useMemo<Edge[]>(() => {
    const ids = ["trigger", ...steps.map((s) => String(s.key))];
    return ids.slice(0, -1).map((source, i) => ({ id: `e-${source}-${ids[i + 1]}`, source, target: ids[i + 1] }));
  }, [steps]);

  const selectedStep = typeof selection === "number" ? steps.find((s) => s.key === selection) ?? null : null;
  const selectedIndex = selectedStep ? steps.findIndex((s) => s.key === selectedStep.key) : -1;

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
          onNodeClick={(_, node) => setSelection(node.type === "trigger" ? "trigger" : Number(node.id))}
          fitView
        >
          <Background />
          <Controls showInteractive={false} />
        </ReactFlow>
      </div>

      <div className="space-y-3 rounded-lg border border-zinc-800 p-4">
        <button
          type="button"
          onClick={onAddStep}
          className="w-full rounded-md border border-zinc-700 px-2.5 py-1 text-xs hover:bg-zinc-900"
        >
          + Add step
        </button>

        {selection === "trigger" ? (
          <div className="space-y-2 text-xs text-zinc-400">
            <div className="text-sm font-medium text-zinc-300">Trigger</div>
            <p>
              What starts this workflow — cron, webhook, RSS, Matrix, and so on. Triggers are configured on the
              workflow&apos;s own page after it&apos;s created (or run it manually with “Run now”).
            </p>
            <p className="text-zinc-600">Steps below run top-to-bottom each time a trigger fires.</p>
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
              onChange={(e) => onUpdateStep(selectedStep.key, { actionType: e.target.value, config: {} })}
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
              onChange={(config) => onUpdateStep(selectedStep.key, { config })}
            />
          </div>
        ) : (
          <p className="text-xs text-zinc-600">Select a step to edit, the trigger node for info, or add a step.</p>
        )}
      </div>
    </div>
  );
}
