import { useQuery } from "@tanstack/react-query";
import { api, type ActionDescriptor } from "../lib/api";

// Built-ins first, plugins alphabetically — shared by the builder dropdown and the Actions page.
export function groupBySource(actions: ActionDescriptor[]): [string, ActionDescriptor[]][] {
  const groups = new Map<string, ActionDescriptor[]>();
  for (const action of actions) {
    const list = groups.get(action.source) ?? [];
    list.push(action);
    groups.set(action.source, list);
  }
  return [...groups.entries()].sort(([a], [b]) =>
    a === "builtin" ? -1 : b === "builtin" ? 1 : a.localeCompare(b),
  );
}

export const sourceLabel = (source: string) =>
  source === "builtin" ? "Built-in" : source.startsWith("plugin:") ? source.slice(7) : source;

// Tiny provenance chip for step lists — hover for the full plugin name.
export function SourceBadge({ actionType }: { actionType: string }) {
  const { data: actions } = useQuery({
    queryKey: ["actions"],
    queryFn: api.actions.list,
    staleTime: 60_000,
  });

  const source = actions?.find((a) => a.type === actionType)?.source;
  if (!source) return null;

  const builtin = source === "builtin";
  return (
    <span
      title={sourceLabel(source)}
      className={
        builtin
          ? "rounded-full border border-emerald-500/40 bg-emerald-500/10 px-1.5 py-0.5 text-[10px] text-emerald-400"
          : "rounded-full border border-sky-500/40 bg-sky-500/10 px-1.5 py-0.5 text-[10px] text-sky-400"
      }
    >
      {builtin ? "core" : "plugin"}
    </span>
  );
}
