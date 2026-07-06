import type { PluginLogLine } from "./api";

const CAP = 500;

// Merge log lines from the poll snapshot and the live stream into one ordered, de-duplicated tail.
// Keyed by the monotonic Seq: dupes (a line seen on both paths) collapse, and a line the best-effort
// stream dropped is healed when the backstop poll re-delivers it (merge is by identity, not max-seq, so
// a lower Seq can still slot in). Returns the same reference when nothing is new to avoid re-renders.
export function mergeLogLines(
  prev: PluginLogLine[],
  incoming: PluginLogLine[],
  cap = CAP,
): PluginLogLine[] {
  if (incoming.length === 0) {
    return prev;
  }

  const bySeq = new Map<number, PluginLogLine>(prev.map((line) => [line.seq, line]));
  let added = false;
  for (const line of incoming) {
    if (!bySeq.has(line.seq)) {
      bySeq.set(line.seq, line);
      added = true;
    }
  }

  if (!added) {
    return prev;
  }

  return [...bySeq.values()].sort((a, b) => a.seq - b.seq).slice(-cap);
}
