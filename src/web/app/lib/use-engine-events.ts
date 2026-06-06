import { HubConnectionBuilder, LogLevel } from "@microsoft/signalr";
import { useEffect, useRef } from "react";

export type EngineEvent = {
  type:
    | "ExecutionStarted"
    | "StepCompleted"
    | "StepFailed"
    | "ExecutionCompleted"
    | "ExecutionFailed";
  payload: { executionId: string } & Record<string, unknown>;
};

export function useEngineEvents(onEvent: (engineEvent: EngineEvent) => void) {
  const handler = useRef(onEvent);
  handler.current = onEvent;

  useEffect(() => {
    const connection = new HubConnectionBuilder()
      .withUrl("/hubs/executions")
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();

    connection.on("engineEvent", (engineEvent: EngineEvent) => handler.current(engineEvent));
    connection.start().catch(console.error);

    return () => {
      connection.stop().catch(() => {});
    };
  }, []);
}
