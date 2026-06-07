import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { api, type CreateWorkflowStep } from "../lib/api";
import { SchemaForm, type JsonSchema } from "./schema-form";
import { groupBySource, sourceKind, sourceLabel } from "./action-source";

type DraftStep = CreateWorkflowStep & { key: number };

const inputClass =
  "w-full rounded-md border border-zinc-700 bg-zinc-900 px-3 py-1.5 text-sm " +
  "placeholder:text-zinc-600 focus:border-emerald-500 focus:outline-none";

let nextKey = 1;

export type WorkflowFormValue = {
  name: string;
  description: string | null;
  steps: CreateWorkflowStep[];
};

// Shared by the create and edit routes — same builder, different mutation.
export function WorkflowForm({
  initial,
  submitLabel,
  pendingLabel,
  pending,
  error,
  onSubmit,
}: {
  initial?: WorkflowFormValue;
  submitLabel: string;
  pendingLabel: string;
  pending: boolean;
  error: unknown;
  onSubmit: (value: WorkflowFormValue) => void;
}) {
  const [name, setName] = useState(initial?.name ?? "");
  const [description, setDescription] = useState(initial?.description ?? "");
  const [steps, setSteps] = useState<DraftStep[]>(
    () => initial?.steps.map((step) => ({ ...step, key: nextKey++ })) ?? [],
  );

  const { data: actions } = useQuery({ queryKey: ["actions"], queryFn: api.actions.list });

  const schemaFor = (actionType: string): JsonSchema | null => {
    const raw = actions?.find((a) => a.type === actionType)?.configSchema;
    return raw ? (JSON.parse(raw) as JsonSchema) : null;
  };

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

      <div className="space-y-3">
        <div className="flex items-center justify-between">
          <h2 className="text-sm font-medium text-zinc-300">Steps</h2>
          <button
            type="button"
            onClick={() =>
              setSteps((current) => [
                ...current,
                { key: nextKey++, actionType: actions?.[0]?.type ?? "", name: null, config: {} },
              ])
            }
            className="rounded-md border border-zinc-700 px-2.5 py-1 text-xs hover:bg-zinc-900"
          >
            Add step
          </button>
        </div>

        {steps.map((step, index) => (
          <div key={step.key} className="rounded-lg border border-zinc-800 p-4">
            <div className="mb-3 flex items-center gap-2">
              <span className="text-xs text-zinc-500">#{index + 1}</span>
              <select
                className={`${inputClass} flex-1`}
                value={step.actionType}
                onChange={(e) => updateStep(step.key, { actionType: e.target.value, config: {} })}
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
              onChange={(config) => updateStep(step.key, { config })}
            />
          </div>
        ))}
      </div>

      {error != null && <p className="text-sm text-red-400">{String(error)}</p>}

      <button
        type="button"
        disabled={!name || pending}
        onClick={() =>
          onSubmit({
            name,
            description: description || null,
            steps: steps.map(({ key: _, ...step }) => step),
          })
        }
        className="rounded-md bg-emerald-600 px-4 py-2 text-sm font-medium text-white hover:bg-emerald-500 disabled:opacity-50"
      >
        {pending ? pendingLabel : submitLabel}
      </button>
    </div>
  );
}
