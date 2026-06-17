import { useState } from "react";
import type { ActionDescriptor, CreateWorkflowStep } from "../lib/api";
import { groupBySource, sourceKind, sourceLabel } from "./action-source";
import { type JsonSchema, SchemaForm } from "./schema-form";
import { FanOutTargets, type KeyEdge, type SwitchRouting, SwitchTargets } from "./switch-routing";
import { type GraphSelection, WorkflowGraph } from "./workflow-graph";
import { type DraftTrigger, newDraftTrigger, TriggerEditor, triggerSummary } from "./workflow-triggers";

type DraftStep = CreateWorkflowStep & {
  key: number;
  routing?: SwitchRouting;
  fanOut?: number[];
};

const fieldClass =
  "w-full rounded-md border border-zinc-700 bg-zinc-900 px-3 py-1.5 text-sm " +
  "placeholder:text-zinc-600 focus:border-emerald-500 focus:outline-none";

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
  stepLabels,
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
  stepLabels: string[];
}) {
  const [selection, setSelection] = useState<GraphSelection>(steps[0]?.key ?? null);

  const displayName = (actionType: string) =>
    actions.find((a) => a.type === actionType)?.displayName ?? actionType;

  const addTrigger = () => {
    const draft = newDraftTrigger();
    onTriggersChange([...triggers, draft]);
    setSelection(`trigger:${draft.key}`);
  };

  const graphSteps = steps.map((s) => ({
    key: s.key,
    label: s.name || displayName(s.actionType),
    actionType: s.actionType,
  }));
  const graphTriggers = triggers.map((t) => ({
    key: t.key,
    label: triggerSummary(t),
    entryStepKey: t.entryStepOrder != null ? steps[t.entryStepOrder]?.key : undefined,
  }));

  const selectedStep =
    typeof selection === "number" ? (steps.find((s) => s.key === selection) ?? null) : null;
  const selectedIndex = selectedStep ? steps.findIndex((s) => s.key === selectedStep.key) : -1;
  const selectedTriggerKey =
    typeof selection === "string" && selection.startsWith("trigger:")
      ? Number(selection.slice("trigger:".length))
      : null;
  const selectedTrigger =
    selectedTriggerKey != null ? (triggers.find((t) => t.key === selectedTriggerKey) ?? null) : null;

  return (
    <div className="grid gap-3 lg:grid-cols-[1fr_22rem]">
      <WorkflowGraph
        steps={graphSteps}
        triggers={graphTriggers}
        stepEdges={stepEdges}
        selection={selection}
        onSelect={setSelection}
      />

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
              stepLabels={stepLabels}
            />
            <p className="text-[11px] text-zinc-600">Fires this workflow. Changes apply when you save.</p>
          </div>
        ) : selectedStep ? (
          <div className="space-y-3">
            <div className="flex items-center justify-between text-xs text-zinc-500">
              <span>Step #{selectedIndex + 1}</span>
              <span className="flex gap-1">
                <button
                  type="button"
                  onClick={() => onMoveStep(selectedIndex, -1)}
                  className="px-1 hover:text-zinc-200"
                >
                  ↑
                </button>
                <button
                  type="button"
                  onClick={() => onMoveStep(selectedIndex, 1)}
                  className="px-1 hover:text-zinc-200"
                >
                  ↓
                </button>
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
                onUpdateStep(selectedStep.key, {
                  actionType: e.target.value,
                  config: {},
                  routing: undefined,
                  fanOut: undefined,
                })
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
            {selectedStep.actionType === "switch" ? (
              <SwitchTargets
                step={selectedStep}
                steps={steps}
                onChange={(routing) => onUpdateStep(selectedStep.key, { routing })}
              />
            ) : (
              <FanOutTargets
                step={selectedStep}
                steps={steps}
                onChange={(fanOut) => onUpdateStep(selectedStep.key, { fanOut })}
              />
            )}
          </div>
        ) : (
          <p className="text-xs text-zinc-600">
            Select a step to edit, a trigger node for info, or add a step.
          </p>
        )}
      </div>
    </div>
  );
}
