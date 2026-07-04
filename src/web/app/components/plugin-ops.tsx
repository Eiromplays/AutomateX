import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useEffect, useRef, useState } from "react";
import { api, type PluginInfo, type PluginLogLine } from "../lib/api";
import { toast } from "./toast";

function stateStyle(state: string): { label: string; className: string } {
  switch (state) {
    case "running":
      return { label: "running", className: "border-emerald-700/60 text-emerald-300" };
    case "exited":
      return { label: "crashed", className: "border-red-800/60 text-red-300" };
    case "disabled":
      return { label: "disabled", className: "border-amber-700/60 text-amber-300" };
    default:
      return { label: "idle", className: "border-zinc-700 text-zinc-400" };
  }
}

const mb = (bytes: number) => `${Math.round(bytes / (1024 * 1024))} MB`;

// Per-plugin ops: a live status badge (state, memory, restarts) plus an expandable console that polls
// the log tail every 2s. Logs are instance-admin only; a 403 just shows a note.
export function PluginOps({ plugin }: { plugin: PluginInfo }) {
  const queryClient = useQueryClient();
  const [open, setOpen] = useState(false);
  const [lines, setLines] = useState<PluginLogLine[]>([]);
  const cursor = useRef(0);
  const state = stateStyle(plugin.state);

  const refresh = () => queryClient.invalidateQueries({ queryKey: ["plugins"] });

  const restart = useMutation({
    mutationFn: () => api.plugins.restart(plugin.name),
    onSuccess: () => {
      refresh();
      toast.success(`${plugin.name} restarting.`);
    },
    onError: (e) => toast.error(String(e)),
  });

  const setEnabled = useMutation({
    mutationFn: (enabled: boolean) => api.plugins.setEnabled(plugin.name, enabled),
    onSuccess: (_r, enabled) => {
      refresh();
      toast.success(enabled ? `${plugin.name} enabled.` : `${plugin.name} disabled.`);
    },
    onError: (e) => toast.error(String(e)),
  });

  const { data, error } = useQuery({
    queryKey: ["plugin-logs", plugin.name],
    queryFn: () => api.plugins.logs(plugin.name, cursor.current),
    enabled: open,
    refetchInterval: open ? 2000 : false,
  });

  useEffect(() => {
    if (data && data.lines.length > 0) {
      cursor.current = data.cursor;
      setLines((prev) => [...prev, ...data.lines].slice(-500));
    }
  }, [data]);

  return (
    <div className="mt-2 space-y-2">
      <div className="flex flex-wrap items-center gap-2 text-xs text-zinc-500">
        <span className={`rounded-full border px-1.5 py-0.5 ${state.className}`}>● {state.label}</span>
        {plugin.state === "running" && <span>{mb(plugin.memoryBytes)}</span>}
        {plugin.restarts > 0 && <span>{plugin.restarts} restart{plugin.restarts === 1 ? "" : "s"}</span>}
        <button type="button" onClick={() => setOpen((o) => !o)} className="text-zinc-500 hover:text-zinc-300">
          {open ? "Hide logs" : "Logs"}
        </button>
        {!plugin.disabled && (
          <button
            type="button"
            disabled={restart.isPending}
            onClick={() => restart.mutate()}
            className="text-zinc-500 hover:text-zinc-300 disabled:opacity-50"
          >
            ↻ Restart
          </button>
        )}
        <button
          type="button"
          disabled={setEnabled.isPending}
          onClick={() => setEnabled.mutate(plugin.disabled)}
          className="text-zinc-500 hover:text-amber-300 disabled:opacity-50"
        >
          {plugin.disabled ? "⏻ Enable" : "⏻ Disable"}
        </button>
      </div>

      {open && (
        <div className="rounded-md border border-zinc-800 bg-zinc-950 p-2">
          {error ? (
            <p className="text-xs text-zinc-500">Logs require instance-admin (or the plugin hasn&apos;t run yet).</p>
          ) : lines.length === 0 ? (
            <p className="text-xs text-zinc-600">No log output yet.</p>
          ) : (
            <pre className="max-h-60 overflow-auto font-mono text-[11px] leading-relaxed text-zinc-300">
              {lines.map((l) => (
                <div key={l.seq} className={l.level === "Error" ? "text-red-400" : undefined}>
                  <span className="text-zinc-600">{new Date(l.at).toLocaleTimeString()} </span>
                  {l.source ? <span className="text-zinc-500">[{l.source}] </span> : null}
                  {l.message}
                </div>
              ))}
            </pre>
          )}
        </div>
      )}
    </div>
  );
}
