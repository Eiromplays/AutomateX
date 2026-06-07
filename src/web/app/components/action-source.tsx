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
  source === "builtin"
    ? "Built-in"
    : source.startsWith("plugin:")
      ? source.slice(7)
      : source.startsWith("workspace:")
        ? source.slice(10)
        : source;

export type SourceKind = "core" | "plugin" | "workspace";

export const sourceKind = (source: string): SourceKind =>
  source === "builtin" ? "core" : source.startsWith("workspace:") ? "workspace" : "plugin";

const kindClass: Record<SourceKind, string> = {
  core: "rounded-full border border-emerald-500/40 bg-emerald-500/10 px-1.5 py-0.5 text-[10px] text-emerald-400",
  plugin: "rounded-full border border-sky-500/40 bg-sky-500/10 px-1.5 py-0.5 text-[10px] text-sky-400",
  workspace:
    "rounded-full border border-amber-500/40 bg-amber-500/10 px-1.5 py-0.5 text-[10px] text-amber-400",
};

export function SourceChip({ source }: { source: string }) {
  return (
    <span title={sourceLabel(source)} className={kindClass[sourceKind(source)]}>
      {sourceKind(source)}
    </span>
  );
}

// Tiny provenance chip for step lists — hover for the full plugin name.
export function SourceBadge({ actionType }: { actionType: string }) {
  const { data: actions } = useQuery({
    queryKey: ["actions"],
    queryFn: api.actions.list,
    staleTime: 60_000,
  });

  const source = actions?.find((a) => a.type === actionType)?.source;
  if (!source) return null;

  return <SourceChip source={source} />;
}
