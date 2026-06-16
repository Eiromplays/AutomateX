import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { api, type CreateWorkflowStep, type WorkflowEdgeInput } from "../lib/api";
import { SchemaForm, type JsonSchema } from "./schema-form";
import { groupBySource, sourceKind, sourceLabel } from "./action-source";
import { WorkflowCanvas } from "./workflow-canvas";
import { FanOutTargets, keyEdges, routingFromEdges, submitEdges, SwitchTargets, type SwitchRouting } from "./switch-routing";
import { TriggersSection, type DraftTrigger } from "./workflow-triggers";

type DraftStep = CreateWorkflowStep & { key: number; routing?: SwitchRouting; fanOut?: number[] };

const inputClass =
  "w-full rounded-md border border-zinc-700 bg-zinc-900 px-3 py-1.5 text-sm " +
  "placeholder:text-zinc-600 focus:border-emerald-500 focus:outline-none";

let nextKey = 1;

export type WorkflowFormValue = {
  name: string;
  description: string | null;
  steps: CreateWorkflowStep[];
  edges?: WorkflowEdgeInput[];
  triggers?: DraftTrigger[];
  continueOnFailure: boolean;
};

// Shared by the create and edit routes — same builder, different mutation.
export function WorkflowForm({
  initial,
  submitLabel,
  pendingLabel,
  pending,
  error,
  onSubmit,
  onCancel,
  hideTriggers,
}: {
  initial?: WorkflowFormValue;
  submitLabel: string;
  pendingLabel: string;
  pending: boolean;
  error: unknown;
  onSubmit: (value: WorkflowFormValue) => void;
  onCancel?: () => void;
  hideTriggers?: boolean;
}) {
  const [name, setName] = useState(initial?.name ?? "");
  const [description, setDescription] = useState(initial?.description ?? "");
  const [continueOnFailure, setContinueOnFailure] = useState(initial?.continueOnFailure ?? false);
  const [triggerDrafts, setTriggerDrafts] = useState<DraftTrigger[]>(initial?.triggers ?? []);
  const [steps, setSteps] = useState<DraftStep[]>(() => {
    const built: DraftStep[] = initial?.steps.map((step) => ({ ...step, key: nextKey++ })) ?? [];
    if (initial?.edges?.length) routingFromEdges(built, initial.edges);
    return built;
  });

  const { data: actions } = useQuery({ queryKey: ["actions"], queryFn: api.actions.list });

  const schemaFor = (actionType: string): JsonSchema | null => {
    const raw = actions?.find((a) => a.type === actionType)?.configSchema;
    return raw ? (JSON.parse(raw) as JsonSchema) : null;
  };

  // Labels in workflow order (index = step order) for the trigger "starts at step" picker.
  const displayName = (actionType: string) => actions?.find((a) => a.type === actionType)?.displayName ?? actionType;
  const stepLabels = steps.map((s, i) => `#${i + 1} ${s.name || displayName(s.actionType)}`);

  const updateStep = (key: number, patch: Partial<DraftStep>) =>
    setSteps((current) => current.map((s) => (s.key === key ? { ...s, ...patch } : s)));

  const moveStep = (index: number, delta: number) =>
    setSteps((current) => {
      const target = index + delta;
      if (target < 0 || target >= current.length) return current;
      const next = [...current];
      [next[index], next[target]] = [next[target], next[index]];
      return next;
    });

  const addStep = () =>
    setSteps((current) => [
      ...current,
      { key: nextKey++, actionType: actions?.[0]?.type ?? "", name: null, config: {} },
    ]);

  const removeStep = (key: number) => setSteps((current) => current.filter((s) => s.key !== key));

  const [mode, setMode] = useState<"canvas" | "form">("canvas");
  const tabClass = (active: boolean) =>
    `px-2 py-1 ${active ? "bg-zinc-800 text-zinc-100" : "text-zinc-400 hover:text-zinc-200"}`;

  return (
    <div className="space-y-4">
      <label className="block">
        <span className="mb-1 block text-xs font-medium text-zinc-400">Name *</span>
        <input className={inputClass} value={name} onChange={(e) => setName(e.target.value)} />
      </label>
      <label className="block">
        <span className="mb-1 block text-xs font-medium text-zinc-400">Description</span>
        <input
          className={inputClass}
          value={description}
          onChange={(e) => setDescription(e.target.value)}
        />
      </label>

      <label className="flex items-start gap-2">
        <input
          type="checkbox"
          className="mt-0.5 accent-emerald-500"
          checked={continueOnFailure}
          onChange={(e) => setContinueOnFailure(e.target.checked)}
        />
        <span>
          <span className="block text-sm text-zinc-300">Continue on step failure</span>
          <span className="block text-[11px] text-zinc-500">
            On by default a failed step fails the whole run. Enable to let independent parallel
            lanes finish; the run still ends Failed.
          </span>
        </span>
      </label>

      <div className="space-y-3">
        <div className="flex items-center justify-between">
          <h2 className="text-sm font-medium text-zinc-300">Steps</h2>
          <div className="flex items-center gap-2">
            <div className="flex overflow-hidden rounded-md border border-zinc-700 text-xs">
              <button type="button" onClick={() => setMode("canvas")} className={tabClass(mode === "canvas")}>
                Canvas
              </button>
              <button type="button" onClick={() => setMode("form")} className={tabClass(mode === "form")}>
                Form
              </button>
            </div>
            {mode === "form" && (
              <button
                type="button"
                onClick={addStep}
                className="rounded-md border border-zinc-700 px-2.5 py-1 text-xs hover:bg-zinc-900"
              >
                Add step
              </button>
            )}
          </div>
        </div>

        {mode === "canvas" && (
          <WorkflowCanvas
            steps={steps}
            stepEdges={keyEdges(steps)}
            actions={actions ?? []}
            schemaFor={schemaFor}
            onUpdateStep={updateStep}
            onMoveStep={moveStep}
            onAddStep={addStep}
            onRemoveStep={removeStep}
            triggers={triggerDrafts}
            onTriggersChange={setTriggerDrafts}
            stepLabels={stepLabels}
          />
        )}

        {mode === "form" &&
          steps.map((step, index) => (
          <div key={step.key} className="rounded-lg border border-zinc-800 p-4">
            <div className="mb-3 flex items-center gap-2">
              <span className="text-xs text-zinc-500">#{index + 1}</span>
              <select
                className={`${inputClass} flex-1`}
                value={step.actionType}
                onChange={(e) => updateStep(step.key, { actionType: e.target.value, config: {}, routing: undefined, fanOut: undefined })}
              >
                {groupBySource(actions ?? []).map(([source, items]) => (
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
              <button type="button" onClick={() => moveStep(index, -1)} className="px-1 text-zinc-500 hover:text-zinc-200">↑</button>
              <button type="button" onClick={() => moveStep(index, 1)} className="px-1 text-zinc-500 hover:text-zinc-200">↓</button>
              <button
                type="button"
                onClick={() => setSteps((current) => current.filter((s) => s.key !== step.key))}
                className="px-1 text-zinc-500 hover:text-red-400"
              >
                ✕
              </button>
            </div>
            <input
              className={`${inputClass} mb-3`}
              placeholder="Step name (optional)"
              value={step.name ?? ""}
              onChange={(e) => updateStep(step.key, { name: e.target.value || null })}
            />
            <SchemaForm
              schema={schemaFor(step.actionType)}
              value={step.config}
              actionType={step.actionType}
              onChange={(config) => updateStep(step.key, { config })}
            />
            <div className="mt-3">
              {step.actionType === "switch" ? (
                <SwitchTargets
                  step={step}
                  steps={steps}
                  onChange={(routing) => updateStep(step.key, { routing })}
                />
              ) : (
                <FanOutTargets
                  step={step}
                  steps={steps}
                  onChange={(fanOut) => updateStep(step.key, { fanOut })}
                />
              )}
            </div>
          </div>
        ))}
      </div>

      {!hideTriggers && mode === "form" && (
        <div className="border-t border-zinc-800 pt-4">
          <TriggersSection triggers={triggerDrafts} onChange={setTriggerDrafts} stepLabels={stepLabels} />
        </div>
      )}

      {error != null && <p className="text-sm text-red-400">{String(error)}</p>}

      <div className="flex gap-2">
        <button
          type="button"
          disabled={!name || pending}
          onClick={() =>
            onSubmit({
              name,
              description: description || null,
              steps: steps.map(({ key: _key, routing: _routing, fanOut: _fanOut, ...step }) => step),
              edges: submitEdges(steps),
              triggers: triggerDrafts,
              continueOnFailure,
            })
          }
          className="rounded-md bg-emerald-600 px-4 py-2 text-sm font-medium text-white hover:bg-emerald-500 disabled:opacity-50"
        >
          {pending ? pendingLabel : submitLabel}
        </button>
        {onCancel && (
          <button
            type="button"
            disabled={pending}
            onClick={onCancel}
            className="rounded-md border border-zinc-700 px-4 py-2 text-sm hover:bg-zinc-900 disabled:opacity-50"
          >
            Cancel
          </button>
        )}
      </div>
    </div>
  );
}
