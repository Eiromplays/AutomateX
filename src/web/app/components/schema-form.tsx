import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { api } from "../lib/api";
import { toast } from "./toast";

// Renders a form from the JSON Schema the engine exports for each action's config
// type (GET /api/actions). Strings, numbers and booleans get native inputs; anything
// deeper falls back to a raw JSON textarea.
export type JsonSchema = {
  type?: string | string[];
  format?: string;
  properties?: Record<string, JsonSchema>;
  required?: string[];
  default?: unknown;
};

type SchemaFormProps = {
  schema: JsonSchema | null;
  value: Record<string, unknown>;
  onChange: (value: Record<string, unknown>) => void;
};

type FieldKind = "number" | "boolean" | "text" | "json";

function kindOf(schema: JsonSchema): FieldKind {
  const types = [schema.type ?? []].flat();
  if (types.includes("integer") || types.includes("number")) return "number";
  if (types.includes("boolean")) return "boolean";
  if (types.includes("object") || types.includes("array")) return "json";
  return "text";
}

const inputClass =
  "w-full rounded-md border border-zinc-700 bg-zinc-900 px-3 py-1.5 text-sm " +
  "placeholder:text-zinc-600 focus:border-emerald-500 focus:outline-none";

const isConnectionRef = (value: unknown) =>
  typeof value === "string" && value.includes("{{connections.");

// Inserts {{connections.<name>.<field>}} so users don't hand-type (and mis-case) them.
// Always available — even with no connections, it offers a path to create one without
// losing the page (opens /connections in a new tab; the list refetches on focus back).
function ConnectionInserter({ onInsert }: { onInsert: (token: string) => void }) {
  const { data: connections } = useQuery({
    queryKey: ["connections"],
    queryFn: api.connections.list,
    staleTime: 60_000,
    refetchOnWindowFocus: true,
  });

  const usable = connections?.filter((c) => c.secretKeys.length > 0) ?? [];
  const [creating, setCreating] = useState(false);

  return (
    <>
      {/* Sits inside the input's right edge: bare 🔗 trigger, no border/arrow. */}
      <select
        title="Insert a connection reference"
        className="w-6 cursor-pointer appearance-none bg-transparent text-center text-sm text-zinc-500 hover:text-emerald-400 focus:outline-none"
        value=""
        onChange={(e) => {
          const selected = e.target.value;
          e.target.value = "";
          if (!selected) return;
          if (selected === "__new__") {
            setCreating(true);
            return;
          }
          onInsert(selected);
        }}
      >
        <option value="">🔗</option>
        {usable.map((c) => (
          <optgroup key={c.id} label={c.name}>
            {c.secretKeys.map((k) => (
              <option key={k} value={`{{connections.${c.name}.${k}}}`}>
                {k}
              </option>
            ))}
          </optgroup>
        ))}
        <option value="__new__">＋ New connection…</option>
      </select>
      {creating && <ConnectionCreateModal onClose={() => setCreating(false)} onInsert={onInsert} />}
    </>
  );
}

// Create a connection without leaving the builder. Known types render guided fields;
// custom (free-form) connections still live on the Connections page.
function ConnectionCreateModal({
  onClose,
  onInsert,
}: {
  onClose: () => void;
  onInsert: (token: string) => void;
}) {
  const queryClient = useQueryClient();
  const { data: types } = useQuery({ queryKey: ["connection-types"], queryFn: api.connections.types });
  const [name, setName] = useState("");
  const [typeKey, setTypeKey] = useState("");
  const [values, setValues] = useState<Record<string, string>>({});

  const selectedType = types?.find((t) => t.type === typeKey) ?? null;

  const create = useMutation({
    mutationFn: () =>
      api.connections.create({
        name,
        provider: typeKey || null,
        secrets: Object.fromEntries(Object.entries(values).filter(([, v]) => v !== "")),
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["connections"] });
      toast.success(`Connection "${name}" created.`);
      // Drop a reference to the first field in so the user isn't left re-selecting it.
      const firstKey = selectedType?.fields[0]?.key;
      if (firstKey) onInsert(`{{connections.${name}.${firstKey}}}`);
      onClose();
    },
    onError: (error) => toast.error(`Create failed — ${String(error)}`),
  });

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4" onClick={onClose}>
      <div
        className="w-full max-w-md space-y-3 rounded-lg border border-zinc-700 bg-zinc-900 p-5"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex items-center justify-between">
          <h2 className="text-sm font-medium text-zinc-200">New connection</h2>
          <button type="button" onClick={onClose} className="text-zinc-500 hover:text-zinc-200">
            ✕
          </button>
        </div>

        <label className="block">
          <span className="mb-1 block text-xs text-zinc-400">Name *</span>
          <input className={inputClass} value={name} onChange={(e) => setName(e.target.value)} placeholder="e.g. my-discord" />
        </label>

        <label className="block">
          <span className="mb-1 block text-xs text-zinc-400">Type *</span>
          <select
            className={inputClass}
            value={typeKey}
            onChange={(e) => {
              setTypeKey(e.target.value);
              setValues({});
            }}
          >
            <option value="">Select a type…</option>
            {types?.map((t) => (
              <option key={t.type} value={t.type}>
                {t.displayName}
              </option>
            ))}
          </select>
        </label>

        {selectedType?.fields.map((field) => (
          <label key={field.key} className="block">
            <span className="mb-1 flex items-center gap-1 text-xs text-zinc-400">
              {field.label}
              {field.required && <span className="text-emerald-400">*</span>}
            </span>
            <input
              type={field.secret ? "password" : "text"}
              className={inputClass}
              value={values[field.key] ?? ""}
              onChange={(e) => setValues((current) => ({ ...current, [field.key]: e.target.value }))}
            />
            {field.helpText && <span className="mt-0.5 block text-[11px] text-zinc-600">{field.helpText}</span>}
          </label>
        ))}

        <p className="text-[11px] text-zinc-600">
          Need a custom (free-form) connection? Use the{" "}
          <a href="/connections" target="_blank" rel="noopener" className="text-emerald-400 hover:underline">
            Connections page
          </a>
          .
        </p>

        <button
          type="button"
          disabled={!name || !typeKey || create.isPending}
          onClick={() => create.mutate()}
          className="w-full rounded-md bg-emerald-600 px-4 py-2 text-sm font-medium text-white hover:bg-emerald-500 disabled:opacity-50"
        >
          {create.isPending ? "Creating…" : "Create connection"}
        </button>
      </div>
    </div>
  );
}

export function SchemaForm({ schema, value, onChange }: SchemaFormProps) {
  if (!schema?.properties) {
    return (
      <textarea
        className={`${inputClass} font-mono`}
        rows={4}
        placeholder="{ } — raw JSON config"
        value={JSON.stringify(value, null, 2)}
        onChange={(e) => {
          try {
            onChange(JSON.parse(e.target.value));
          } catch {
            // keep last valid value while the user is typing
          }
        }}
      />
    );
  }

  const required = new Set(schema.required ?? []);

  // Keys in the config that the active action's schema doesn't know — typically
  // plugin-version drift. Preserved on save, but ignored at execution time.
  const unknownKeys = Object.keys(value).filter((key) => !(key in (schema.properties ?? {})));

  const set = (key: string, fieldValue: unknown) => onChange({ ...value, [key]: fieldValue });
  const append = (key: string, token: string) => set(key, `${value[key] === undefined ? "" : String(value[key])}${token}`);

  return (
    <div className="space-y-3">
      {Object.entries(schema.properties).map(([key, property]) => {
        const kind = kindOf(property);
        return (
          <label key={key} className="block">
            <span className="mb-1 flex items-center gap-2 text-xs font-medium text-zinc-400">
              {key}
              {required.has(key) && <span className="text-emerald-400">*</span>}
              {isConnectionRef(value[key]) && (
                <span className="text-[10px] text-sky-400" title="Uses a connection reference">
                  🔗 connection
                </span>
              )}
            </span>
            {kind === "boolean" ? (
              <input
                type="checkbox"
                className="size-4 accent-emerald-500"
                checked={Boolean(value[key])}
                onChange={(e) => set(key, e.target.checked)}
              />
            ) : kind === "number" ? (
              <input
                type="number"
                className={inputClass}
                value={value[key] === undefined ? "" : String(value[key])}
                onChange={(e) => set(key, e.target.value === "" ? undefined : Number(e.target.value))}
              />
            ) : kind === "json" ? (
              <textarea
                className={`${inputClass} font-mono`}
                rows={3}
                value={value[key] === undefined ? "" : JSON.stringify(value[key], null, 2)}
                onChange={(e) => {
                  try {
                    set(key, JSON.parse(e.target.value));
                  } catch {
                    // ignore until valid
                  }
                }}
              />
            ) : (
              <div className="relative">
                <input
                  type="text"
                  className={`${inputClass} pr-10`}
                  value={value[key] === undefined ? "" : String(value[key])}
                  onChange={(e) => set(key, e.target.value === "" ? undefined : e.target.value)}
                />
                <div className="absolute inset-y-0 right-1.5 flex items-center">
                  <ConnectionInserter onInsert={(token) => append(key, token)} />
                </div>
              </div>
            )}
          </label>
        );
      })}
      {unknownKeys.length > 0 && (
        <p className="text-xs text-amber-400">
          ⚠ Not in the current action's schema: {unknownKeys.join(", ")} — kept in the config but
          ignored at execution (plugin version drift?).
        </p>
      )}
    </div>
  );
}
