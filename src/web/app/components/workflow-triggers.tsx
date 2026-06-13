import { useQuery } from "@tanstack/react-query";
import { api, type WorkflowTrigger } from "../lib/api";
import { SchemaForm, type JsonSchema } from "./schema-form";

// A trigger being authored in the builder. `id` present = it already exists (edit); absent = new.
export type DraftTrigger = {
  key: number;
  id?: string;
  type: string;
  config: Record<string, unknown>;
  enabled: boolean;
};

let nextTriggerKey = 1;

const inputClass =
  "w-full rounded-md border border-zinc-700 bg-zinc-900 px-3 py-1.5 text-sm " +
  "placeholder:text-zinc-600 focus:border-emerald-500 focus:outline-none";

const BUILTIN_TYPES = [
  { type: "cron", label: "Cron schedule" },
  { type: "webhook", label: "Webhook" },
  { type: "workflow", label: "After another workflow" },
];

function defaultConfig(type: string): Record<string, unknown> {
  if (type === "cron") return { cron: "*/5 * * * *" };
  if (type === "workflow") return { workflowId: "", on: "succeeded" };
  return {};
}

// Short label for a trigger node / chip.
export function triggerSummary(draft: DraftTrigger): string {
  const config = draft.config;
  if (draft.type === "cron" && typeof config.cron === "string") return `cron · ${config.cron}`;
  if (typeof config.url === "string") return `${draft.type} · ${config.url}`;
  if (draft.type === "workflow") return "after workflow";
  return draft.type;
}

export function triggersFromWorkflow(triggers: WorkflowTrigger[]): DraftTrigger[] {
  return triggers.map((t) => {
    let config: Record<string, unknown> = {};
    try {
      config = JSON.parse(t.configJson) as Record<string, unknown>;
    } catch {
      config = {};
    }
    return { key: nextTriggerKey++, id: t.id, type: t.type, config, enabled: t.enabled };
  });
}

export function newDraftTrigger(): DraftTrigger {
  return { key: nextTriggerKey++, type: "cron", config: defaultConfig("cron"), enabled: true };
}

// Trigger drafts from an import document's triggers (so they show, editable, in the builder).
export function importedDraftTriggers(triggers: { type?: string; config?: Record<string, unknown> }[]): DraftTrigger[] {
  return triggers
    .filter((t) => typeof t.type === "string")
    .map((t) => ({ key: nextTriggerKey++, type: t.type as string, config: t.config ?? {}, enabled: true }));
}

// Persists the builder's trigger drafts against a saved workflow: deletes removed ones, creates
// new ones, and updates changed config/enabled — reusing the trigger endpoints so the webhook
// secret, cron next-run and chain validation stay server-side. Returns any new webhook URLs.
export async function applyTriggers(
  workflowId: string,
  drafts: DraftTrigger[],
  before: DraftTrigger[],
): Promise<string[]> {
  const secrets: string[] = [];
  const keptIds = new Set(drafts.filter((d) => d.id).map((d) => d.id!));

  for (const original of before) {
    if (original.id && !keptIds.has(original.id)) {
      await api.triggers.remove(original.id);
    }
  }

  for (const draft of drafts) {
    if (!draft.id) {
      const created = await api.triggers.create(workflowId, { type: draft.type, config: draft.config });
      if (created.webhookUrl) secrets.push(created.webhookUrl);
      continue;
    }

    const original = before.find((b) => b.id === draft.id);
    if (!original) continue;

    // Webhook config is immutable (holds the secret); only its enabled flag can change.
    const configChanged = draft.type !== "webhook" && JSON.stringify(original.config) !== JSON.stringify(draft.config);
    const enabledChanged = original.enabled !== draft.enabled;
    if (configChanged) {
      await api.triggers.update(draft.id, { config: draft.config, enabled: draft.enabled });
    } else if (enabledChanged) {
      await api.triggers.update(draft.id, { enabled: draft.enabled });
    }
  }

  return secrets;
}

// Editor for a single trigger draft — used both in the Form-mode list and the Canvas side panel.
export function TriggerEditor({
  draft,
  onChange,
  onRemove,
}: {
  draft: DraftTrigger;
  onChange: (draft: DraftTrigger) => void;
  onRemove: () => void;
}) {
  const { data: triggerTypes } = useQuery({ queryKey: ["trigger-types"], queryFn: api.triggers.types, staleTime: 60_000 });
  const { data: workflows } = useQuery({ queryKey: ["workflows"], queryFn: api.workflows.list, staleTime: 60_000 });

  const pluginTypes = (triggerTypes ?? [])
    .filter((t) => t.source !== "builtin")
    .map((t) => ({ type: t.type, label: t.displayName }));
  const typeOptions = [...BUILTIN_TYPES, ...pluginTypes];
  const schemaFor = (type: string): JsonSchema | null => {
    const raw = triggerTypes?.find((t) => t.type === type)?.configSchema;
    return raw ? (JSON.parse(raw) as JsonSchema) : null;
  };

  const set = (patch: Partial<DraftTrigger>) => onChange({ ...draft, ...patch });
  const setConfig = (config: Record<string, unknown>) => set({ config });

  return (
    <div className="space-y-2">
      <div className="flex items-center gap-2">
        {draft.id ? (
          <span className="rounded-full border border-zinc-700 px-2 py-0.5 text-xs text-zinc-300">{draft.type}</span>
        ) : (
          <select
            className={`${inputClass} flex-1`}
            value={draft.type}
            onChange={(e) => set({ type: e.target.value, config: defaultConfig(e.target.value) })}
          >
            {typeOptions.map((o) => (
              <option key={o.type} value={o.type}>
                {o.label}
              </option>
            ))}
          </select>
        )}
        <label className="flex items-center gap-1 text-xs text-zinc-400">
          <input
            type="checkbox"
            className="size-4 accent-emerald-500"
            checked={draft.enabled}
            onChange={(e) => set({ enabled: e.target.checked })}
          />
          enabled
        </label>
        <button type="button" onClick={onRemove} className="px-1 text-zinc-500 hover:text-red-400">
          ✕
        </button>
      </div>

      {draft.type === "cron" ? (
        <input
          className={inputClass}
          placeholder="*/5 * * * *"
          value={typeof draft.config.cron === "string" ? draft.config.cron : ""}
          onChange={(e) => setConfig({ ...draft.config, cron: e.target.value })}
        />
      ) : draft.type === "webhook" ? (
        <p className="text-[11px] text-amber-500/80">
          {draft.id
            ? "⚠ The secret URL was shown once at creation — rotate it on the workflow page if it was lost."
            : "⚠ A secret URL is generated on save and shown only once — copy it then. If lost, rotate it later."}
        </p>
      ) : draft.type === "workflow" ? (
        <div className="flex gap-2">
          <select
            className={`${inputClass} flex-1`}
            value={typeof draft.config.workflowId === "string" ? draft.config.workflowId : ""}
            onChange={(e) => setConfig({ ...draft.config, workflowId: e.target.value })}
          >
            <option value="">Select a workflow…</option>
            {(workflows ?? []).map((w) => (
              <option key={w.id} value={w.id}>
                {w.name}
              </option>
            ))}
          </select>
          <select
            className={`${inputClass} w-32`}
            value={typeof draft.config.on === "string" ? draft.config.on : "succeeded"}
            onChange={(e) => setConfig({ ...draft.config, on: e.target.value })}
          >
            <option value="succeeded">on success</option>
            <option value="failed">on failure</option>
          </select>
        </div>
      ) : (
        <SchemaForm schema={schemaFor(draft.type)} value={draft.config} onChange={setConfig} />
      )}
    </div>
  );
}

// Linear list of trigger editors — used in the builder's Form mode.
export function TriggersSection({
  triggers,
  onChange,
}: {
  triggers: DraftTrigger[];
  onChange: (triggers: DraftTrigger[]) => void;
}) {
  const update = (key: number, draft: DraftTrigger) => onChange(triggers.map((t) => (t.key === key ? draft : t)));
  const remove = (key: number) => onChange(triggers.filter((t) => t.key !== key));

  return (
    <div className="space-y-3">
      <div className="flex items-center justify-between">
        <h2 className="text-sm font-medium text-zinc-300">Triggers</h2>
        <button
          type="button"
          onClick={() => onChange([...triggers, newDraftTrigger()])}
          className="rounded-md border border-zinc-700 px-2.5 py-1 text-xs hover:bg-zinc-900"
        >
          Add trigger
        </button>
      </div>
      <p className="text-xs text-zinc-500">What starts this workflow. Changes apply when you save.</p>

      {triggers.length === 0 && (
        <p className="text-xs text-zinc-600">No triggers yet — add one, or run the workflow manually with “Run now”.</p>
      )}

      {triggers.map((trigger) => (
        <div key={trigger.key} className="rounded-lg border border-zinc-800 p-3">
          <TriggerEditor draft={trigger} onChange={(d) => update(trigger.key, d)} onRemove={() => remove(trigger.key)} />
        </div>
      ))}
    </div>
  );
}
