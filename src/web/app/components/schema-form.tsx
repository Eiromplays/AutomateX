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

  return (
    <div className="space-y-3">
      {Object.entries(schema.properties).map(([key, property]) => {
        const kind = kindOf(property);
        return (
          <label key={key} className="block">
            <span className="mb-1 block text-xs font-medium text-zinc-400">
              {key}
              {required.has(key) && <span className="ml-1 text-emerald-400">*</span>}
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
              <input
                type="text"
                className={inputClass}
                value={value[key] === undefined ? "" : String(value[key])}
                onChange={(e) => set(key, e.target.value === "" ? undefined : e.target.value)}
              />
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
