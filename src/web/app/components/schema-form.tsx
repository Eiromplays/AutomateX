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
  // The active action type, so a few actions (e.g. switch) can swap in a purpose-built
  // editor for a field instead of the generic JSON fallback.
  actionType?: string;
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

// A single switch case carries exactly one operator. The backend ANDs multiple operators
// per case, but one-per-row covers the routing use and stays legible; power users can still
// reach the others via export/import.
type SwitchCaseValue = {
  label?: string;
  equals?: string;
  notEquals?: string;
  contains?: string;
  isTruthy?: boolean;
};

type SwitchOp = "equals" | "notEquals" | "contains" | "truthy" | "falsy";

const SWITCH_OP_LABELS: Record<SwitchOp, string> = {
  equals: "equals",
  notEquals: "not equals",
  contains: "contains",
  truthy: "is truthy",
  falsy: "is falsy",
};

const SWITCH_OPS_NEEDING_VALUE: SwitchOp[] = ["equals", "notEquals", "contains"];

function switchOpOf(c: SwitchCaseValue): SwitchOp {
  if (c.equals !== undefined) return "equals";
  if (c.notEquals !== undefined) return "notEquals";
  if (c.contains !== undefined) return "contains";
  if (c.isTruthy === false) return "falsy";
  return "truthy";
}

function switchComparisonOf(c: SwitchCaseValue): string {
  return c.equals ?? c.notEquals ?? c.contains ?? "";
}

function buildSwitchCase(label: string, op: SwitchOp, comparison: string): SwitchCaseValue {
  switch (op) {
    case "equals":
      return { label, equals: comparison };
    case "notEquals":
      return { label, notEquals: comparison };
    case "contains":
      return { label, contains: comparison };
    case "truthy":
      return { label, isTruthy: true };
    case "falsy":
      return { label, isTruthy: false };
  }
}

// Row-based editor for a switch step's cases — each row is label + one operator (+ a
// comparison value for equals/notEquals/contains). The labels here are the outgoing
// edge labels you wire to target steps on the canvas.
function SwitchCasesEditor({
  value,
  onChange,
}: {
  value: unknown;
  onChange: (cases: SwitchCaseValue[]) => void;
}) {
  const cases: SwitchCaseValue[] = Array.isArray(value) ? (value as SwitchCaseValue[]) : [];
  const update = (index: number, next: SwitchCaseValue) =>
    onChange(cases.map((c, i) => (i === index ? next : c)));
  const remove = (index: number) => onChange(cases.filter((_, i) => i !== index));
  const add = () => onChange([...cases, { label: "", equals: "" }]);

  return (
    <div className="space-y-2">
      {cases.map((c, index) => {
        const op = switchOpOf(c);
        const label = c.label ?? "";
        const comparison = switchComparisonOf(c);
        return (
          <div key={index} className="space-y-1.5 rounded-md border border-zinc-800 p-2">
            <div className="flex items-center gap-1.5">
              <input
                className={`${inputClass} flex-1`}
                placeholder="label (e.g. paid)"
                value={label}
                onChange={(e) => update(index, buildSwitchCase(e.target.value, op, comparison))}
              />
              <button
                type="button"
                onClick={() => remove(index)}
                className="px-1 text-zinc-500 hover:text-red-400"
                title="Remove case"
              >
                ✕
              </button>
            </div>
            <select
              className={inputClass}
              value={op}
              onChange={(e) => update(index, buildSwitchCase(label, e.target.value as SwitchOp, comparison))}
            >
              {(Object.keys(SWITCH_OP_LABELS) as SwitchOp[]).map((o) => (
                <option key={o} value={o}>
                  {SWITCH_OP_LABELS[o]}
                </option>
              ))}
            </select>
            {SWITCH_OPS_NEEDING_VALUE.includes(op) && (
              <input
                className={inputClass}
                placeholder="value to compare"
                value={comparison}
                onChange={(e) => update(index, buildSwitchCase(label, op, e.target.value))}
              />
            )}
          </div>
        );
      })}
      <button
        type="button"
        onClick={add}
        className="rounded-md border border-zinc-700 px-2.5 py-1 text-xs hover:bg-zinc-900"
      >
        + Add case
      </button>
      <p className="text-[11px] text-zinc-600">
        Tried top to bottom; first match wins. No match falls through to{" "}
        <code className="text-zinc-400">default</code>. On the Canvas, connect each label (and{" "}
        <code className="text-zinc-400">default</code>) to the step it should run.
      </p>
    </div>
  );
}

// Guided editor for mcp.call: pick a stored MCP server connection, list its tools live, and
// render a form for the chosen tool's JSON-Schema arguments (reusing SchemaForm). serverUrl/
// token are stored as template refs to the connection so the action stays connection-agnostic.
function McpCallEditor({
  value,
  onChange,
}: {
  value: Record<string, unknown>;
  onChange: (value: Record<string, unknown>) => void;
}) {
  const { data: connections } = useQuery({
    queryKey: ["connections"],
    queryFn: api.connections.list,
    staleTime: 60_000,
    refetchOnWindowFocus: true,
  });
  const mcpConnections = (connections ?? []).filter((c) => c.provider === "mcp");

  const serverUrl = typeof value.serverUrl === "string" ? value.serverUrl : "";
  const selectedName = serverUrl.match(/\{\{connections\.([^.}]+)\.serverUrl\}\}/)?.[1] ?? null;
  const selectedConn = mcpConnections.find((c) => c.name === selectedName) ?? null;

  const tools = useQuery({
    queryKey: ["mcp-tools", selectedConn?.id],
    queryFn: () => api.connections.mcpTools(selectedConn!.id),
    enabled: selectedConn != null,
    retry: false,
    staleTime: 30_000,
  });

  const tool = typeof value.tool === "string" ? value.tool : "";
  const selectedTool = tools.data?.find((t) => t.name === tool) ?? null;

  let argSchema: JsonSchema | null = null;
  if (selectedTool) {
    try {
      argSchema = JSON.parse(selectedTool.inputSchema) as JsonSchema;
    } catch {
      argSchema = null;
    }
  }

  const set = (patch: Record<string, unknown>) => onChange({ ...value, ...patch });

  const pickServer = (name: string) => {
    const conn = mcpConnections.find((c) => c.name === name);
    if (!conn) {
      set({ serverUrl: "", token: "", tool: "", arguments: {} });
      return;
    }
    set({
      serverUrl: `{{connections.${conn.name}.serverUrl}}`,
      token: conn.secretKeys.includes("token") ? `{{connections.${conn.name}.token}}` : "",
      tool: "",
      arguments: {},
    });
  };

  return (
    <div className="space-y-3">
      <label className="block">
        <span className="mb-1 block text-xs font-medium text-zinc-400">MCP server</span>
        <select className={inputClass} value={selectedName ?? ""} onChange={(e) => pickServer(e.target.value)}>
          <option value="">Select an MCP connection…</option>
          {mcpConnections.map((c) => (
            <option key={c.id} value={c.name}>
              {c.name}
            </option>
          ))}
        </select>
        {mcpConnections.length === 0 && (
          <span className="mt-1 block text-[11px] text-zinc-600">
            No MCP connections yet — create one (type “MCP server”) on the{" "}
            <a href="/connections" target="_blank" rel="noopener" className="text-emerald-400 hover:underline">
              Connections page
            </a>
            .
          </span>
        )}
      </label>

      {selectedConn && (
        <label className="block">
          <span className="mb-1 block text-xs font-medium text-zinc-400">Tool</span>
          {tools.isLoading ? (
            <span className="text-xs text-zinc-500">Loading tools…</span>
          ) : tools.error ? (
            <span className="text-xs text-red-400">Couldn’t list tools — {String(tools.error)}</span>
          ) : (
            <select className={inputClass} value={tool} onChange={(e) => set({ tool: e.target.value, arguments: {} })}>
              <option value="">Select a tool…</option>
              {tools.data?.map((t) => (
                <option key={t.name} value={t.name}>
                  {t.name}
                </option>
              ))}
            </select>
          )}
          {selectedTool?.description && (
            <span className="mt-1 block text-[11px] text-zinc-600">{selectedTool.description}</span>
          )}
        </label>
      )}

      {selectedTool && (
        <div className="block">
          <span className="mb-1 block text-xs font-medium text-zinc-400">Arguments</span>
          <SchemaForm
            schema={argSchema}
            value={(value.arguments as Record<string, unknown>) ?? {}}
            onChange={(args) => set({ arguments: args })}
          />
        </div>
      )}
    </div>
  );
}

export function SchemaForm({ schema, value, onChange, actionType }: SchemaFormProps) {
  if (actionType === "mcp.call") {
    return <McpCallEditor value={value} onChange={onChange} />;
  }

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
        if (actionType === "switch" && key === "cases") {
          return (
            <div key={key} className="block">
              <span className="mb-1 flex items-center gap-2 text-xs font-medium text-zinc-400">
                {key}
                {required.has(key) && <span className="text-emerald-400">*</span>}
              </span>
              <SwitchCasesEditor value={value[key]} onChange={(cases) => set(key, cases)} />
            </div>
          );
        }
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
