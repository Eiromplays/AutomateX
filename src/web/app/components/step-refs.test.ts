import { describe, expect, it } from "vitest";
import {
  assignStepKeys,
  checkStepRefs,
  configHasIndexRef,
  hasStepRef,
  rewriteConfigIndexRefs,
  rewriteIndexRefs,
  type StepLite,
  slugifyStepKey,
} from "./step-refs";

const steps: StepLite[] = [
  { key: "probe-api", order: 0, name: "Probe API" },
  { key: "ssh-deploy", order: 1, name: "SSH deploy" },
];

describe("slugifyStepKey", () => {
  it("slugs names like the server", () => {
    expect(slugifyStepKey("SSH deploy", 0)).toBe("ssh-deploy");
    expect(slugifyStepKey("  Send   Notification!! ", 0)).toBe("send-notification");
    expect(slugifyStepKey("Healthy?", 0)).toBe("healthy");
  });

  it("falls back to position when unsluggable", () => {
    expect(slugifyStepKey(null, 2)).toBe("step-3");
    expect(slugifyStepKey("???", 0)).toBe("step-1");
  });
});

describe("assignStepKeys", () => {
  it("derives keys from names", () => {
    expect(assignStepKeys([{ name: "Probe API" }, { name: "Healthy?" }])).toEqual(["probe-api", "healthy"]);
  });

  it("dedups duplicate names", () => {
    expect(assignStepKeys([{ name: "Notify" }, { name: "Notify" }, { name: "Notify" }])).toEqual([
      "notify",
      "notify-2",
      "notify-3",
    ]);
  });

  it("honours an explicit key", () => {
    expect(assignStepKeys([{ name: "Deploy the thing", key: "deploy" }])).toEqual(["deploy"]);
  });
});

describe("hasStepRef", () => {
  it("detects step refs", () => {
    expect(hasStepRef("{{steps.ssh-deploy.output.stdout}}")).toBe(true);
    expect(hasStepRef("plain")).toBe(false);
    expect(hasStepRef(42)).toBe(false);
  });
});

describe("checkStepRefs", () => {
  it("returns none for non-strings and plain text", () => {
    expect(checkStepRefs(undefined, steps).status).toBe("none");
    expect(checkStepRefs("just text", steps).status).toBe("none");
  });

  it("resolves a known key", () => {
    expect(checkStepRefs("{{steps.ssh-deploy.output.stdout}}", steps)).toEqual({
      status: "ok",
      unknown: [],
      fragile: [],
    });
  });

  it("flags numeric refs as fragile", () => {
    expect(checkStepRefs("{{steps.1.output.stdout}}", steps)).toEqual({
      status: "fragile",
      unknown: [],
      fragile: ["1"],
    });
  });

  it("flags unknown keys and out-of-range orders", () => {
    expect(checkStepRefs("{{steps.nope.output}}", steps).status).toBe("unknown");
    expect(checkStepRefs("{{steps.9.output}}", steps).status).toBe("unknown");
  });
});

describe("rewriteIndexRefs", () => {
  it("rewrites numeric refs to keys", () => {
    expect(rewriteIndexRefs("got {{steps.1.output.stdout}} now", steps)).toBe(
      "got {{steps.ssh-deploy.output.stdout}} now",
    );
  });

  it("leaves key refs and unknown orders alone", () => {
    expect(rewriteIndexRefs("{{steps.ssh-deploy.output.x}}", steps)).toBe("{{steps.ssh-deploy.output.x}}");
    expect(rewriteIndexRefs("{{steps.9.output}}", steps)).toBe("{{steps.9.output}}");
  });
});

describe("config helpers", () => {
  it("detects index refs anywhere in a config", () => {
    expect(configHasIndexRef({ message: "got {{steps.1.output.stdout}}" })).toBe(true);
    expect(configHasIndexRef({ message: "{{steps.ssh-deploy.output.stdout}}" })).toBe(false);
    expect(configHasIndexRef({ message: "plain" })).toBe(false);
  });

  it("rewrites index refs across a config object, preserving structure", () => {
    expect(rewriteConfigIndexRefs({ message: "{{steps.1.output.stdout}}", priority: 2 }, steps)).toEqual({
      message: "{{steps.ssh-deploy.output.stdout}}",
      priority: 2,
    });
  });
});
