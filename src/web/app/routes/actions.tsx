import { useQuery } from "@tanstack/react-query";
import { api } from "../lib/api";
import { groupBySource, sourceLabel } from "../components/action-source";

function configFields(raw: string | null): { name: string; required: boolean }[] {
  if (!raw) return [];
  try {
    const schema = JSON.parse(raw) as { properties?: Record<string, unknown>; required?: string[] };
    const required = new Set(schema.required ?? []);
    return Object.keys(schema.properties ?? {}).map((name) => ({
      name,
      required: required.has(name),
    }));
  } catch {
    return [];
  }
}

export default function Actions() {
  const { data: actions } = useQuery({ queryKey: ["actions"], queryFn: api.actions.list });

  if (!actions) return <p className="text-sm text-zinc-500">Loading…</p>;

  return (
    <div className="max-w-3xl">
      <div className="mb-6 flex items-baseline justify-between">
        <h1 className="text-lg font-semibold">Actions</h1>
        <span className="text-xs text-zinc-500">
          {actions.length} installed — drop plugins in <code className="text-zinc-400">plugins/</code> and
          restart to add more
        </span>
      </div>

      <div className="space-y-8">
        {groupBySource(actions).map(([source, items]) => (
          <section key={source}>
            <h2 className="mb-3 flex items-center gap-2 text-sm font-medium text-zinc-300">
              {sourceLabel(source)}
              <span
                className={
                  source === "builtin"
                    ? "rounded-full border border-emerald-500/40 bg-emerald-500/10 px-2 py-0.5 text-[10px] text-emerald-400"
                    : "rounded-full border border-sky-500/40 bg-sky-500/10 px-2 py-0.5 text-[10px] text-sky-400"
                }
              >
                {source === "builtin" ? "core" : "plugin"}
              </span>
            </h2>

            <div className="space-y-2">
              {items.map((action) => (
                <div key={action.type} className="rounded-lg border border-zinc-800 p-4">
                  <div className="mb-1 flex items-center gap-2">
                    <span className="text-sm font-medium">{action.displayName}</span>
                    <code className="rounded bg-zinc-900 px-1.5 py-0.5 text-xs text-zinc-400">
                      {action.type}
                    </code>
                  </div>
                  {action.description && (
                    <p className="mb-2 text-xs text-zinc-500">{action.description}</p>
                  )}
                  {configFields(action.configSchema).length > 0 && (
                    <div className="flex flex-wrap gap-1.5">
                      {configFields(action.configSchema).map((field) => (
                        <span
                          key={field.name}
                          className="rounded border border-zinc-800 bg-zinc-900 px-1.5 py-0.5 text-[11px] text-zinc-400"
                        >
                          {field.name}
                          {field.required && <span className="text-emerald-400"> *</span>}
                        </span>
                      ))}
                    </div>
                  )}
                </div>
              ))}
            </div>
          </section>
        ))}
      </div>
    </div>
  );
}
