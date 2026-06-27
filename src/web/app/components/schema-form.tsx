import { useQuery } from "@tanstack/react-query";
import { useEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";
import { api } from "../lib/api";
import { ConnectionAutocompleteField } from "./connection-autocomplete-field";
import { ConnectionForm } from "./connection-form";
import { filterConnections } from "./connection-form-logic";
import { checkConnectionRefs, hasConnectionRef } from "./connection-refs";
import { fieldKind, type JsonSchema } from "./schema-fields";
import { checkStepRefs, type StepOutput, stepInsertGroups } from "./step-refs";
import { Dialog, DialogContent } from "./ui/dialog";

// Renders a form from the JSON Schema the engine exports for each action's config
// type (GET /api/actions). Strings, numbers and booleans get native inputs; anything
// deeper falls back to a raw JSON textarea. Field-to-control mapping lives in
// ./schema-fields (fieldKind) so it can be unit-tested in isolation.
export type { JsonSchema } from "./schema-fields";

type SchemaFormProps = {
  schema: JsonSchema | null;
  value: Record<string, unknown>;
  onChange: (value: Record<string, unknown>) => void;
  // The active action type, so a few actions (e.g. switch) can swap in a purpose-built
  // editor for a field instead of the generic JSON fallback.
  actionType?: string;
  // Sibling steps (keys + orders + output fields) for validating and autocompleting
  // {{steps.<key>.output…}} references; stepOrder is this step's order (upstream filter).
  stepRefs?: StepOutput[];
  stepOrder?: number;
};

const inputClass =
  "w-full rounded-md border border-zinc-700 bg-zinc-900 px-3 py-1.5 text-sm " +
  "placeholder:text-zinc-600 focus:border-emerald-500 focus:outline-none";

// Inserts {{connections.<name>.<field>}} so users don't hand-type (and mis-case) them.
// Always available — even with no connections, it offers a path to create one without
// losing the page (opens /connections in a new tab; the list refetches on focus back).
const PANEL_WIDTH = 256;
const PANEL_MAX_HEIGHT = 320;

function ReferenceInserter({
  onInsert,
  steps,
  stepOrder,
}: {
  onInsert: (token: string) => void;
  steps?: StepOutput[];
  stepOrder?: number;
}) {
  const { data: connections } = useQuery({
    queryKey: ["connections"],
    queryFn: api.connections.list,
    staleTime: 60_000,
    refetchOnWindowFocus: true,
  });

  const hasStepOptions = steps ? stepInsertGroups(steps, stepOrder, "").length > 0 : false;

  const [creating, setCreating] = useState(false);
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState("");
  const [tab, setTab] = useState<"steps" | "connections">(hasStepOptions ? "steps" : "connections");
  const [pos, setPos] = useState<{ top: number; left: number } | null>(null);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const panelRef = useRef<HTMLDivElement>(null);

  const usable = (connections ?? []).filter((c) => c.secretKeys.length > 0);
  const matches = filterConnections(usable, query);
  const stepGroups = steps ? stepInsertGroups(steps, stepOrder, query) : [];
  const showSteps = hasStepOptions && tab === "steps";

  // Anchor the (portaled) panel to the trigger, clamped into the viewport so it never renders
  // off-screen near a low/right-edge field.
  const place = () => {
    const rect = triggerRef.current?.getBoundingClientRect();
    if (!rect) return;
    const left = Math.min(Math.max(8, rect.right - PANEL_WIDTH), window.innerWidth - PANEL_WIDTH - 8);
    const top = Math.min(rect.bottom + 4, window.innerHeight - PANEL_MAX_HEIGHT - 8);
    setPos({ top: Math.max(8, top), left: Math.max(8, left) });
  };

  // Portaled to body so an ancestor's overflow can't clip it. While open: close on Escape or a
  // pointer-down outside trigger+panel, and re-anchor on scroll/resize so it tracks the field.
  useEffect(() => {
    if (!open) {
      setQuery("");
      return;
    }
    const onPointerDown = (e: PointerEvent) => {
      const target = e.target as Node;
      if (!triggerRef.current?.contains(target) && !panelRef.current?.contains(target)) setOpen(false);
    };
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") setOpen(false);
    };
    const reposition = () => place();
    document.addEventListener("pointerdown", onPointerDown);
    document.addEventListener("keydown", onKey);
    // Capture so scrolls in inner containers (not just the window) re-anchor the panel.
    window.addEventListener("scroll", reposition, true);
    window.addEventListener("resize", reposition);
    return () => {
      document.removeEventListener("pointerdown", onPointerDown);
      document.removeEventListener("keydown", onKey);
      window.removeEventListener("scroll", reposition, true);
      window.removeEventListener("resize", reposition);
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open]);

  const toggle = () => {
    if (open) {
      setOpen(false);
      return;
    }
    place();
    setOpen(true);
  };

  const insert = (token: string) => {
    onInsert(token);
    setOpen(false);
  };

  // Enter in the search box inserts the first item of the active tab.
  const insertFirstMatch = () => {
    if (showSteps) {
      const firstStep = stepGroups[0]?.items[0];
      if (firstStep) insert(firstStep.token);
      return;
    }
    const key = matches[0]?.secretKeys[0];
    if (matches[0] && key) insert(`{{connections.${matches[0].name}.${key}}}`);
  };

  return (
    <>
      {/* Bare 🔗 trigger, no border/arrow — sits inside the input's right edge. */}
      <button
        ref={triggerRef}
        type="button"
        title="Insert a step output or connection reference"
        onClick={toggle}
        className="w-6 text-center text-sm text-zinc-500 hover:text-emerald-400 focus:outline-none"
      >
        🔗
      </button>

      {open &&
        pos &&
        createPortal(
          <div
            ref={panelRef}
            style={{
              position: "fixed",
              top: pos.top,
              left: pos.left,
              width: PANEL_WIDTH,
            }}
            className="z-50 rounded-md border border-zinc-700 bg-zinc-900 p-1 shadow-xl"
          >
            {hasStepOptions && (
              <div className="mb-1 flex overflow-hidden rounded border border-zinc-700 text-xs">
                {(["steps", "connections"] as const).map((t) => (
                  <button
                    key={t}
                    type="button"
                    onClick={() => setTab(t)}
                    className={`flex-1 px-2 py-1 ${tab === t ? "bg-zinc-800 text-zinc-100" : "text-zinc-400 hover:text-zinc-200"}`}
                  >
                    {t === "steps" ? "⛓ Steps" : "🔗 Connections"}
                  </button>
                ))}
              </div>
            )}
            <input
              autoFocus
              value={query}
              onChange={(e) => setQuery(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === "Enter") {
                  e.preventDefault();
                  insertFirstMatch();
                }
              }}
              placeholder={showSteps ? "Search step outputs…" : "Search connections…"}
              className="mb-1 w-full rounded border border-zinc-700 bg-zinc-950 px-2 py-1 text-xs placeholder:text-zinc-600 focus:border-emerald-500 focus:outline-none"
            />
            <div className="max-h-56 overflow-y-auto">
              {showSteps ? (
                <>
                  {stepGroups.length === 0 && (
                    <p className="px-2 py-1.5 text-xs text-zinc-600">No matches.</p>
                  )}
                  {stepGroups.map((group) => (
                    <div key={group.key} className="mb-1">
                      <div className="px-2 py-0.5 text-[10px] font-medium uppercase tracking-wide text-zinc-500">
                        ⛓ {group.name || group.key}
                      </div>
                      {group.items.map((item) => (
                        <button
                          type="button"
                          key={item.token}
                          onClick={() => insert(item.token)}
                          className="block w-full rounded px-2 py-1 text-left text-xs text-zinc-300 hover:bg-zinc-800"
                        >
                          {item.label}
                        </button>
                      ))}
                    </div>
                  ))}
                </>
              ) : (
                <>
                  {matches.length === 0 && (
                    <p className="px-2 py-1.5 text-xs text-zinc-600">
                      {usable.length === 0 ? "No connections with secrets yet." : "No matches."}
                    </p>
                  )}
                  {matches.map((c) => (
                    <div key={c.id} className="mb-1">
                      <div className="px-2 py-0.5 text-[10px] font-medium uppercase tracking-wide text-zinc-500">
                        {c.name}
                      </div>
                      {c.secretKeys.map((k) => (
                        <button
                          type="button"
                          key={k}
                          onClick={() => insert(`{{connections.${c.name}.${k}}}`)}
                          className="block w-full rounded px-2 py-1 text-left text-xs text-zinc-300 hover:bg-zinc-800"
                        >
                          {k}
                        </button>
                      ))}
                    </div>
                  ))}
                </>
              )}
            </div>
            {!showSteps && (
              <button
                type="button"
                onClick={() => {
                  setOpen(false);
                  setCreating(true);
                }}
                className="mt-1 block w-full rounded border-t border-zinc-800 px-2 py-1.5 text-left text-xs text-emerald-400 hover:bg-zinc-800"
              >
                ＋ New connection…
              </button>
            )}
          </div>,
          document.body,
        )}

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
  return (
    <Dialog open onOpenChange={(open) => (open ? undefined : onClose())}>
      <DialogContent title="New connection">
        {/* Same form as the Connections page (ConnectionForm), so typed + free-form + custom
            fields all work here. On save we drop a reference to the first field and close. */}
        <ConnectionForm
          onSaved={(saved, firstKey) => {
            if (firstKey) onInsert(`{{connections.${saved.name}.${firstKey}}}`);
            onClose();
          }}
          onCancel={onClose}
        />
      </DialogContent>
    </Dialog>
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
        <select
          className={inputClass}
          value={selectedName ?? ""}
          onChange={(e) => pickServer(e.target.value)}
        >
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
            <a
              href="/connections"
              target="_blank"
              rel="noopener"
              className="text-emerald-400 hover:underline"
            >
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
            <select
              className={inputClass}
              value={tool}
              onChange={(e) => set({ tool: e.target.value, arguments: {} })}
            >
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

// Guided editor for llm.agent: the LLM fields plus a checklist of MCP server connections that
// become the agent's tool sources (stored as templated serverUrl/token entries in mcpServers).
function LlmAgentEditor({
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

  const set = (patch: Record<string, unknown>) => onChange({ ...value, ...patch });
  const str = (key: string, fallback = "") =>
    typeof value[key] === "string" ? (value[key] as string) : fallback;

  const servers = Array.isArray(value.mcpServers)
    ? (value.mcpServers as { serverUrl?: string; token?: string }[])
    : [];
  const selectedNames = new Set<string>(
    servers.flatMap((s) => {
      const match =
        typeof s.serverUrl === "string"
          ? s.serverUrl.match(/\{\{connections\.([^.}]+)\.serverUrl\}\}/)
          : null;
      return match ? [match[1]] : [];
    }),
  );

  const toggleServer = (name: string, hasToken: boolean) => {
    if (selectedNames.has(name)) {
      set({
        mcpServers: servers.filter(
          (s) => !(typeof s.serverUrl === "string" && s.serverUrl.includes(`connections.${name}.serverUrl`)),
        ),
      });
    } else {
      const entry: { serverUrl: string; token?: string } = {
        serverUrl: `{{connections.${name}.serverUrl}}`,
      };
      if (hasToken) entry.token = `{{connections.${name}.token}}`;
      set({ mcpServers: [...servers, entry] });
    }
  };

  return (
    <div className="space-y-3">
      <label className="block">
        <span className="mb-1 block text-xs font-medium text-zinc-400">Goal *</span>
        <textarea
          className={`${inputClass} font-normal`}
          rows={2}
          placeholder="What should the agent accomplish?"
          value={str("goal")}
          onChange={(e) => set({ goal: e.target.value })}
        />
      </label>
      <label className="block">
        <span className="mb-1 block text-xs font-medium text-zinc-400">System prompt</span>
        <textarea
          className={inputClass}
          rows={2}
          value={str("system")}
          onChange={(e) => set({ system: e.target.value || undefined })}
        />
      </label>
      <div className="grid grid-cols-2 gap-2">
        <label className="block">
          <span className="mb-1 block text-xs font-medium text-zinc-400">Model *</span>
          <input
            className={inputClass}
            placeholder="gpt-4o-mini"
            value={str("model")}
            onChange={(e) => set({ model: e.target.value })}
          />
        </label>
        <label className="block">
          <span className="mb-1 block text-xs font-medium text-zinc-400">Max iterations</span>
          <input
            type="number"
            className={inputClass}
            value={typeof value.maxIterations === "number" ? value.maxIterations : 8}
            onChange={(e) =>
              set({
                maxIterations: e.target.value === "" ? undefined : Number(e.target.value),
              })
            }
          />
        </label>
      </div>
      <label className="block">
        <span className="mb-1 block text-xs font-medium text-zinc-400">Base URL</span>
        <input
          className={inputClass}
          value={str("baseUrl", "https://api.openai.com")}
          onChange={(e) => set({ baseUrl: e.target.value })}
        />
      </label>
      <label className="block">
        <span className="mb-1 block text-xs font-medium text-zinc-400">API key</span>
        <input
          className={inputClass}
          placeholder="{{connections.openai.apiKey}}"
          value={str("apiKey")}
          onChange={(e) => set({ apiKey: e.target.value || undefined })}
        />
      </label>
      <div className="block">
        <span className="mb-1 block text-xs font-medium text-zinc-400">Tool servers (MCP)</span>
        {mcpConnections.length === 0 ? (
          <span className="text-[11px] text-zinc-600">
            No MCP connections yet — create one (type “MCP server”) on the{" "}
            <a
              href="/connections"
              target="_blank"
              rel="noopener"
              className="text-emerald-400 hover:underline"
            >
              Connections page
            </a>
            .
          </span>
        ) : (
          <div className="space-y-1 rounded-md border border-zinc-800 bg-zinc-900/40 p-2">
            {mcpConnections.map((c) => (
              <label key={c.id} className="flex items-center gap-2 text-sm text-zinc-300">
                <input
                  type="checkbox"
                  className="size-4 accent-emerald-500"
                  checked={selectedNames.has(c.name)}
                  onChange={() => toggleServer(c.name, c.secretKeys.includes("token"))}
                />
                {c.name}
              </label>
            ))}
          </div>
        )}
      </div>
    </div>
  );
}

// workflow.call: pick a target workflow (in this workspace) and an optional payload. The run pauses
// here until the called workflow finishes; its result is this step's output.
function WorkflowCallEditor({
  value,
  onChange,
}: {
  value: Record<string, unknown>;
  onChange: (value: Record<string, unknown>) => void;
}) {
  const { data: workflows } = useQuery({ queryKey: ["workflows"], queryFn: api.workflows.list, staleTime: 60_000 });
  const set = (patch: Record<string, unknown>) => onChange({ ...value, ...patch });
  const workflowId = typeof value.workflowId === "string" ? value.workflowId : "";
  const payload = typeof value.payload === "string" ? value.payload : "";

  return (
    <div className="space-y-3">
      <label className="block">
        <span className="mb-1 flex items-center gap-2 text-xs font-medium text-zinc-400">
          Workflow to call <span className="text-emerald-400">*</span>
        </span>
        <select
          className={inputClass}
          value={workflowId}
          onChange={(e) => set({ workflowId: e.target.value || undefined })}
        >
          <option value="">— pick a workflow —</option>
          {(workflows ?? []).map((w) => (
            <option key={w.id} value={w.id}>
              {w.name}
            </option>
          ))}
        </select>
      </label>
      <label className="block">
        <span className="mb-1 block text-xs font-medium text-zinc-400">Payload (optional)</span>
        <textarea
          className={`${inputClass} font-mono`}
          rows={3}
          placeholder="JSON or {{steps.…}} — becomes the called workflow's {{trigger.payload}}"
          value={payload}
          onChange={(e) => set({ payload: e.target.value || undefined })}
        />
      </label>
      <p className="text-[11px] text-zinc-600">
        The run pauses here until the called workflow finishes; its result becomes this step's output
        (<code>{"{{steps.<name>.output.status}}"}</code>).
      </p>
    </div>
  );
}

export function SchemaForm({ schema, value, onChange, actionType, stepRefs, stepOrder }: SchemaFormProps) {
  // For validating {{connections.…}} refs in field values. undefined while loading → neutral chip.
  const { data: connections } = useQuery({
    queryKey: ["connections"],
    queryFn: api.connections.list,
    staleTime: 60_000,
  });

  if (actionType === "mcp.call") {
    return <McpCallEditor value={value} onChange={onChange} />;
  }

  if (actionType === "llm.agent") {
    return <LlmAgentEditor value={value} onChange={onChange} />;
  }

  if (actionType === "workflow.call") {
    return <WorkflowCallEditor value={value} onChange={onChange} />;
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
  const append = (key: string, token: string) =>
    set(key, `${value[key] === undefined ? "" : String(value[key])}${token}`);

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
        const kind = fieldKind(property);
        const refCheck = connections ? checkConnectionRefs(value[key], connections) : null;
        const stepCheck = stepRefs ? checkStepRefs(value[key], stepRefs) : null;
        const isEmptyRequired =
          required.has(key) && kind !== "boolean" && (value[key] === undefined || value[key] === "");
        return (
          <label key={key} className="block">
            <span className="mb-1 flex items-center gap-2 text-xs font-medium text-zinc-400">
              {key}
              {required.has(key) && <span className="text-emerald-400">*</span>}
              {isEmptyRequired && <span className="text-[10px] text-amber-400">required</span>}
              {connections == null && hasConnectionRef(value[key]) && (
                <span className="text-[10px] text-sky-400" title="Uses a connection reference">
                  🔗 connection
                </span>
              )}
              {refCheck?.status === "ok" && (
                <span className="text-[10px] text-emerald-400" title="Connection reference resolves">
                  🔗 connection
                </span>
              )}
              {refCheck?.status === "unknown" && (
                <span
                  className="text-[10px] text-amber-400"
                  title={`Unknown connection or key: ${refCheck.unknown.join(", ")}`}
                >
                  🔗 unknown: {refCheck.unknown.join(", ")}
                </span>
              )}
              {stepCheck?.status === "ok" && (
                <span className="text-[10px] text-emerald-400" title="Step reference resolves">
                  ⛓ step
                </span>
              )}
              {stepCheck?.status === "fragile" && (
                <span
                  className="text-[10px] text-amber-400"
                  title={`Index-based step reference (#${stepCheck.fragile.join(", #")}) — breaks on reorder. Convert to names.`}
                >
                  ⛓ index — convert to name
                </span>
              )}
              {stepCheck?.status === "unknown" && (
                <span
                  className="text-[10px] text-red-400"
                  title={`Unknown step: ${stepCheck.unknown.join(", ")}`}
                >
                  ⛓ unknown step: {stepCheck.unknown.join(", ")}
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
            ) : kind === "multiline" ? (
              <div className="relative">
                <ConnectionAutocompleteField
                  multiline
                  className={`${inputClass} min-h-[4.5rem] pr-8`}
                  value={value[key] === undefined ? "" : String(value[key])}
                  onChange={(v) => set(key, v === "" ? undefined : v)}
                  connections={connections ?? []}
                  steps={stepRefs}
                  stepOrder={stepOrder}
                />
                <div className="absolute right-1.5 top-1.5 flex items-center">
                  <ReferenceInserter
                    onInsert={(token) => append(key, token)}
                    steps={stepRefs}
                    stepOrder={stepOrder}
                  />
                </div>
              </div>
            ) : (
              <div className="relative">
                <ConnectionAutocompleteField
                  className={`${inputClass} pr-10`}
                  value={value[key] === undefined ? "" : String(value[key])}
                  onChange={(v) => set(key, v === "" ? undefined : v)}
                  connections={connections ?? []}
                  steps={stepRefs}
                  stepOrder={stepOrder}
                />
                <div className="absolute inset-y-0 right-1.5 flex items-center">
                  <ReferenceInserter
                    onInsert={(token) => append(key, token)}
                    steps={stepRefs}
                    stepOrder={stepOrder}
                  />
                </div>
              </div>
            )}
          </label>
        );
      })}
      {unknownKeys.length > 0 && (
        <p className="text-xs text-amber-400">
          ⚠ Not in the current action's schema: {unknownKeys.join(", ")} — kept in the config but ignored at
          execution (plugin version drift?).
        </p>
      )}
    </div>
  );
}
