import { describe, expect, it } from "vitest";
import { stepAutocompleteQuery, stepCompletions } from "./step-autocomplete";
import type { StepOutput } from "./step-refs";

const steps: StepOutput[] = [
  { key: "probe-api", order: 0, name: "Probe API", fields: ["statusCode", "body"] },
  { key: "ssh-deploy", order: 1, name: "SSH deploy", fields: ["exitCode", "stdout", "stderr"] },
  { key: "open-output", order: 2, name: "Open", fields: [] },
];

describe("stepAutocompleteQuery", () => {
  it("detects an open step token at the caret", () => {
    const text = "x {{steps.ssh";
    expect(stepAutocompleteQuery(text, text.length)).toEqual({ start: 2, query: "ssh" });
  });

  it("detects a drill-in partial", () => {
    const text = "{{steps.ssh-deploy.output.std";
    expect(stepAutocompleteQuery(text, text.length)).toEqual({ start: 0, query: "ssh-deploy.output.std" });
  });

  it("returns null once the token is closed or broken", () => {
    expect(stepAutocompleteQuery("{{steps.ssh}}", 12)).toBeNull();
    expect(stepAutocompleteQuery("no token here", 5)).toBeNull();
  });
});

describe("stepCompletions", () => {
  it("suggests upstream step keys for a bare prefix (open token to drill into fields)", () => {
    const out = stepCompletions(steps, "ssh", 2);
    expect(out).toEqual([{ name: "steps", key: "ssh-deploy", token: "{{steps.ssh-deploy.output" }]);
  });

  it("closes the token at the step when its schema is open (nothing to drill)", () => {
    const out = stepCompletions(steps, "open", 3);
    expect(out).toEqual([{ name: "steps", key: "open-output", token: "{{steps.open-output.output}}" }]);
  });

  it("excludes the current and later steps", () => {
    // From order 1, only probe-api (order 0) is upstream.
    expect(stepCompletions(steps, "", 1).map((c) => c.key)).toEqual(["probe-api"]);
  });

  it("offers whole-output plus schema fields when drilling in", () => {
    const out = stepCompletions(steps, "ssh-deploy.output.", 2);
    expect(out.map((c) => c.token)).toEqual([
      "{{steps.ssh-deploy.output}}",
      "{{steps.ssh-deploy.output.exitCode}}",
      "{{steps.ssh-deploy.output.stdout}}",
      "{{steps.ssh-deploy.output.stderr}}",
    ]);
  });

  it("filters fields by the typed partial", () => {
    const out = stepCompletions(steps, "ssh-deploy.output.std", 2);
    expect(out.map((c) => c.key)).toEqual(["stdout", "stderr"]);
  });

  it("downgrades to whole-output only when the schema is open", () => {
    const out = stepCompletions(steps, "open-output.output.", 3);
    expect(out.map((c) => c.token)).toEqual(["{{steps.open-output.output}}"]);
  });

  it("returns nothing for an unknown step", () => {
    expect(stepCompletions(steps, "nope.output.", 3)).toEqual([]);
  });
});
