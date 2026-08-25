import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Power, RotateCw } from "lucide-react";
import { useEffect, useRef, useState } from "react";
import { api, type PluginInfo, type PluginLogLine } from "../lib/api";
import { mergeLogLines } from "../lib/plugin-logs";
import { usePluginLogs } from "../lib/use-plugin-logs";
import { Lamp } from "./rack";
import { toast } from "./toast";

function stateStyle(state: string): { label: string; className: string } {
  switch (state) {
    // The lamp carries the state; the label stays achromatic (DESIGN.md).
    case "running":
      return { label: "running", className: "border-engrave text-fg-body" };
    case "exited":
      return { label: "crashed", className: "border-engrave text-fg-body" };
    case "disabled":
      return { label: "disabled", className: "border-engrave text-fg-body" };
    default:
      return { label: "idle", className: "border-engrave text-fg-body" };
  }
}

const mb = (bytes: number) => `${Math.round(bytes / (1024 * 1024))} MB`;

// Per-plugin ops: a live status badge (state, memory, restarts) plus an expandable console. The console
// snapshots the log tail via a 2s poll, then tails live over SignalR; both paths reconcile by Seq, and
// the poll doubles as the reconnect backstop. Logs are instance-admin only; a 403 just shows a note.
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

  // Poll snapshot/backstop: advance the poll cursor (kept independent of the stream so a re-poll can heal
  // any line the stream dropped) and reconcile by Seq.
  useEffect(() => {
    if (data && data.lines.length > 0) {
      cursor.current = data.cursor;
      setLines((prev) => mergeLogLines(prev, data.lines));
    }
  }, [data]);

  // Live tail: stream lines while the console is open, reconciled by Seq against the polled snapshot.
  usePluginLogs(plugin.name, open, (line) => setLines((prev) => mergeLogLines(prev, [line])));

  return (
    <div className="mt-2 space-y-2">
      <div className="flex flex-wrap items-center gap-2 text-xs text-muted-foreground">
        <span className={`inline-flex items-center gap-1.5 rounded-[2px] border px-1.5 py-0.5 ${state.className}`}>
          <Lamp status={plugin.state} /> {state.label}
        </span>
        {plugin.state === "running" && <span>{mb(plugin.memoryBytes)}</span>}
        {plugin.restarts > 0 && <span>{plugin.restarts} restart{plugin.restarts === 1 ? "" : "s"}</span>}
        <button type="button" onClick={() => setOpen((o) => !o)} className="text-muted-foreground hover:text-fg-body">
          {open ? "Hide logs" : "Logs"}
        </button>
        {!plugin.disabled && (
          <button
            type="button"
            disabled={restart.isPending}
            onClick={() => restart.mutate()}
            className="inline-flex items-center gap-1 text-muted-foreground hover:text-fg-body disabled:text-muted-foreground"
          >
            <RotateCw className="h-3 w-3" aria-hidden="true" /> Restart
          </button>
        )}
        <button
          type="button"
          disabled={setEnabled.isPending}
          onClick={() => setEnabled.mutate(plugin.disabled)}
          className="inline-flex items-center gap-1 text-muted-foreground hover:text-amber-300 disabled:text-muted-foreground"
        >
          <Power className="h-3 w-3" aria-hidden="true" /> {plugin.disabled ? "Enable" : "Disable"}
        </button>
      </div>

      {open && (
        <div className="rounded-[2px] border border-line bg-background p-2">
          {error ? (
            <p className="text-xs text-muted-foreground max-w-[38em]">Logs require instance-admin (or the plugin hasn&apos;t run yet).</p>
          ) : lines.length === 0 ? (
            <p className="text-xs text-muted-foreground max-w-[38em]">No log output yet.</p>
          ) : (
            <pre className="max-h-60 overflow-auto font-mono text-[11px] leading-relaxed text-fg-body">
              {lines.map((l) => (
                <div key={l.seq} className={l.level === "Error" ? "text-danger" : undefined}>
                  <span className="text-muted-foreground">{new Date(l.at).toLocaleTimeString()} </span>
                  {l.source ? <span className="text-muted-foreground">[{l.source}] </span> : null}
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
