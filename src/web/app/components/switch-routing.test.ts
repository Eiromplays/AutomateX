import { describe, expect, it } from "vitest";
import {
  backboneEdges,
  isBranched,
  keyEdges,
  routingFromEdges,
  submitEdges,
  validFanOut,
  type RoutingStep,
} from "./switch-routing";

// Keys are deliberately not equal to indices, so any code that confuses the two is caught.
const KEY = [10, 20, 30, 40, 50];

function probe(index: number, name: string): RoutingStep {
  return { key: KEY[index], actionType: "test.probe", name, config: {} };
}

function switchStep(index: number, labels: string[]): RoutingStep {
  return {
    key: KEY[index],
    actionType: "switch",
    name: "switch",
    config: { cases: labels.map((label) => ({ label, equals: label })) },
  };
}

// A fresh copy stripped of authored routing/fanOut — the state right after a reload, before
// routingFromEdges re-derives intent from the persisted edges.
function reload(steps: RoutingStep[]): RoutingStep[] {
  return steps.map((s) => ({ key: s.key, actionType: s.actionType, name: s.name, config: s.config }));
}

describe("linear workflows stay unbranched", () => {
  const steps = [probe(0, "a"), probe(1, "b"), probe(2, "c")];

  it("is not branched and emits no submit edges", () => {
    expect(isBranched(steps)).toBe(false);
    expect(submitEdges(steps)).toEqual([]);
  });

  it("shows an implicit order backbone in the canvas", () => {
    expect(keyEdges(steps)).toEqual([
      { sourceKey: KEY[0], targetKey: KEY[1], label: null },
      { sourceKey: KEY[1], targetKey: KEY[2], label: null },
    ]);
  });
});

describe("parallel fan-out diamond", () => {
  // 0 fork ⇉ 1 lane-a, 2 lane-b ; 1 → 3 join, 2 → 3 join.
  function build(): RoutingStep[] {
    const steps = [probe(0, "fork"), probe(1, "lane-a"), probe(2, "lane-b"), probe(3, "join")];
    steps[0].fanOut = [KEY[1], KEY[2]];
    steps[1].fanOut = [KEY[3]];
    steps[2].fanOut = [KEY[3]];
    return steps;
  }

  it("is branched", () => {
    expect(isBranched(build())).toBe(true);
  });

  it("emits both lanes and a single join, no spurious lane-a→lane-b edge", () => {
    expect(submitEdges(build())).toEqual([
      { from: 0, to: 1, label: null },
      { from: 0, to: 2, label: null },
      { from: 1, to: 3, label: null },
      { from: 2, to: 3, label: null },
    ]);
  });

  it("round-trips through persisted edges", () => {
    const original = submitEdges(build());
    const loaded = reload(build());
    routingFromEdges(loaded, original);
    expect(submitEdges(loaded)).toEqual(original);
  });
});

describe("switch diamond with an explicit join", () => {
  // 0 switch (a → lane-a, default → lane-d) ; both lanes fan out to 3 merge.
  function build(): RoutingStep[] {
    const steps = [switchStep(0, ["a"]), probe(1, "lane-a"), probe(2, "lane-d"), probe(3, "merge")];
    steps[0].routing = { byLabel: { a: KEY[1] }, default: KEY[2] };
    steps[1].fanOut = [KEY[3]];
    steps[2].fanOut = [KEY[3]];
    return steps;
  }

  it("emits labelled fork edges plus the join", () => {
    expect(submitEdges(build())).toEqual([
      { from: 0, to: 1, label: "a" },
      { from: 0, to: 2, label: "default" },
      { from: 1, to: 3, label: null },
      { from: 2, to: 3, label: null },
    ]);
  });

  it("round-trips through persisted edges", () => {
    const original = submitEdges(build());
    const loaded = reload(build());
    routingFromEdges(loaded, original);
    expect(submitEdges(loaded)).toEqual(original);
  });
});

describe("switch lane backbone", () => {
  // 0 switch (a → lane-a terminal, default → lane-d) ; lane-d continues by order to lane-d2.
  function build(): RoutingStep[] {
    const steps = [switchStep(0, ["a"]), probe(1, "lane-a"), probe(2, "lane-d"), probe(3, "lane-d2")];
    steps[0].routing = { byLabel: { a: KEY[1] }, default: KEY[2] };
    return steps;
  }

  it("cuts the backbone before a switch lane head but keeps the within-lane order link", () => {
    expect(submitEdges(build())).toEqual([
      { from: 0, to: 1, label: "a" },
      { from: 0, to: 2, label: "default" },
      { from: 2, to: 3, label: null },
    ]);
  });

  it("round-trips through persisted edges", () => {
    const original = submitEdges(build());
    const loaded = reload(build());
    routingFromEdges(loaded, original);
    expect(submitEdges(loaded)).toEqual(original);
  });
});

describe("backboneEdges", () => {
  it("chains step keys in order for the read-only graph", () => {
    expect(backboneEdges([0, 1, 2, 3])).toEqual([
      { sourceKey: 0, targetKey: 1, label: null },
      { sourceKey: 1, targetKey: 2, label: null },
      { sourceKey: 2, targetKey: 3, label: null },
    ]);
  });

  it("is empty for zero or one step", () => {
    expect(backboneEdges([])).toEqual([]);
    expect(backboneEdges([7])).toEqual([]);
  });
});

describe("validFanOut", () => {
  it("drops self references and keys of deleted steps", () => {
    const steps = [probe(0, "a"), probe(1, "b")];
    steps[0].fanOut = [KEY[0], KEY[1], 999];
    expect(validFanOut(steps[0], new Set(steps.map((s) => s.key)))).toEqual([KEY[1]]);
  });
});
