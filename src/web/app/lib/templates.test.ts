import { describe, expect, it } from "vitest";
import { templates } from "./templates";

// Guards the bundled starter templates: every one must be a valid automatex v1 document the import
// builder can load — real action types, in-range edges, and unique ids.
const KNOWN_ACTIONS = new Set([
  "http.request",
  "switch",
  "gate",
  "llm.prompt",
  "llm.agent",
  "matrix.send",
  "discord.send",
  "pushover.send",
  "email.send",
  "mcp.call",
  "ssh.command",
]);

describe("starter templates", () => {
  it("have unique ids", () => {
    const ids = templates.map((t) => t.id);
    expect(new Set(ids).size).toBe(ids.length);
  });

  it.each(templates.map((t) => [t.id, t] as const))("%s is a valid v1 document", (_id, template) => {
    const { doc } = template;
    expect(doc.automatex).toBe(1);
    expect(doc.name.length).toBeGreaterThan(0);
    expect(doc.steps.length).toBeGreaterThan(0);

    for (const step of doc.steps) {
      expect(KNOWN_ACTIONS.has(step.actionType)).toBe(true);
    }

    for (const edge of doc.edges ?? []) {
      expect(edge.from).toBeGreaterThanOrEqual(0);
      expect(edge.to).toBeGreaterThanOrEqual(0);
      expect(edge.from).toBeLessThan(doc.steps.length);
      expect(edge.to).toBeLessThan(doc.steps.length);
    }
  });

  it("includes a branching template that exercises switch + fan-out + join", () => {
    const branching = templates.find((t) => t.id === "branching-watchdog");
    expect(branching).toBeDefined();
    const edges = branching!.doc.edges ?? [];
    expect(branching!.doc.continueOnFailure).toBe(true);
    expect(edges.some((e) => e.label === "up")).toBe(true);
    expect(edges.some((e) => e.label === "default")).toBe(true);
    expect(edges.filter((e) => e.to === 6).length).toBe(2); // the join
  });
});
