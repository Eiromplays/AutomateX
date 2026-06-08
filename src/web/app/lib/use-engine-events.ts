import { HubConnectionBuilder, LogLevel } from "@microsoft/signalr";
import { useEffect, useRef } from "react";
import { getWorkspaceId } from "./api";

// The server treats an absent X-Workspace-Id as the Default workspace; match it here
// so the live stream joins the same group that requests are scoped to.
const DEFAULT_WORKSPACE_ID = "00000000-0000-0000-0000-000000000001";

export type EngineEvent = {
  type: "ExecutionStarted" | "StepCompleted" | "StepFailed" | "ExecutionCompleted" | "ExecutionFailed";
  // Events now carry the full (already-masked) record, so consumers can patch caches
  // without a refetch. executionId is always present; the rest depend on the type.
  payload: {
    executionId: string;
    workflowId?: string;
    stepOrder?: number;
    actionType?: string;
    output?: string | null;
    error?: string;
    triggeredBy?: string;
  };
};

export function useEngineEvents(onEvent: (engineEvent: EngineEvent) => void) {
  const handler = useRef(onEvent);
  handler.current = onEvent;

  useEffect(() => {
    const connection = new HubConnectionBuilder()
      // The HttpOnly session cookie authenticates the handshake — nothing in the URL.
      .withUrl("/hubs/executions")
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();

    // Group membership is per-connection, so (re)join on every (re)connect.
    const join = () =>
      connection.invoke("JoinWorkspace", getWorkspaceId() ?? DEFAULT_WORKSPACE_ID).catch(console.error);

    connection.on("engineEvent", (engineEvent: EngineEvent) => handler.current(engineEvent));
    connection.onreconnected(join);
    connection.start().then(join).catch(console.error);

    return () => {
      connection.stop().catch(() => {});
    };
  }, []);
}
