import { useQuery } from "@tanstack/react-query";
import { api } from "../lib/api";

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
function ConnectionInserter({ onInsert }: { onInsert: (token: string) => void }) {
  const { data: connections } = useQuery({
    queryKey: ["connections"],
    queryFn: api.connections.list,
    staleTime: 60_000,
  });

  const usable = connections?.filter((c) => c.secretKeys.length > 0) ?? [];
  if (usable.length === 0) return null;

  return (
    // Sits inside the input's right edge: bare 🔗 trigger, no border/arrow.
    <select
      title="Insert a connection reference"
      className="w-6 cursor-pointer appearance-none bg-transparent text-center text-sm text-zinc-500 hover:text-emerald-400 focus:outline-none"
      value=""
      onChange={(e) => {
        if (e.target.value) onInsert(e.target.value);
        e.target.value = "";
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
    </select>
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
