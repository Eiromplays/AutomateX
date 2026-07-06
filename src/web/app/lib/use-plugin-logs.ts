import { HubConnectionBuilder, LogLevel } from "@microsoft/signalr";
import { useEffect, useRef } from "react";
import type { PluginLogLine } from "./api";

// Live plugin-log tail over SignalR, mirroring useEngineEvents. Instance-admin only — a non-admin's
// JoinPlugin is silently declined server-side (no lines, no error), and the poll remains the snapshot +
// backstop. Only connects while `enabled` (the console is open); appended lines are reconciled by Seq.
export function usePluginLogs(
  pluginName: string,
  enabled: boolean,
  onLine: (line: PluginLogLine) => void,
) {
  const handler = useRef(onLine);
  handler.current = onLine;

  useEffect(() => {
    if (!enabled) {
      return;
    }

    const connection = new HubConnectionBuilder()
      // The HttpOnly session cookie authenticates the handshake — nothing in the URL.
      .withUrl("/hubs/plugin-logs")
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();

    // Group membership is per-connection, so (re)join on every (re)connect.
    const join = () => connection.invoke("JoinPlugin", pluginName).catch(console.error);

    connection.on("pluginLog", (line: PluginLogLine) => handler.current(line));
    connection.onreconnected(join);
    connection.start().then(join).catch(console.error);

    return () => {
      connection.stop().catch(() => {});
    };
  }, [pluginName, enabled]);
}
