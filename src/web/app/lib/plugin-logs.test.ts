import { describe, expect, it } from "vitest";
import type { PluginLogLine } from "./api";
import { mergeLogLines } from "./plugin-logs";

const line = (seq: number, message = `m${seq}`): PluginLogLine => ({
  seq,
  at: "2026-07-06T00:00:00Z",
  level: "Information",
  source: null,
  message,
});

describe("mergeLogLines", () => {
  it("appends new lines in seq order", () => {
    const result = mergeLogLines([line(1), line(2)], [line(3), line(4)]);
    expect(result.map((l) => l.seq)).toEqual([1, 2, 3, 4]);
  });

  it("de-dupes a line delivered by both the stream and the poll", () => {
    const result = mergeLogLines([line(1), line(2)], [line(2), line(3)]);
    expect(result.map((l) => l.seq)).toEqual([1, 2, 3]);
  });

  it("heals a stream-dropped line when the backstop poll re-delivers it", () => {
    // Stream skipped seq 2, so the tail jumped 1 -> 3; the poll returns 2 and 3 again.
    const streamed = [line(1), line(3)];
    const result = mergeLogLines(streamed, [line(2), line(3)]);
    expect(result.map((l) => l.seq)).toEqual([1, 2, 3]);
  });

  it("caps the tail at the requested size, keeping the newest", () => {
    const prev = [line(1), line(2)];
    const result = mergeLogLines(prev, [line(3), line(4), line(5)], 3);
    expect(result.map((l) => l.seq)).toEqual([3, 4, 5]);
  });

  it("returns the same reference when nothing is new", () => {
    const prev = [line(1), line(2)];
    expect(mergeLogLines(prev, [])).toBe(prev);
    expect(mergeLogLines(prev, [line(1), line(2)])).toBe(prev);
  });
});
