import { StatusChip } from "./rack";

/*
 * Semantic execution/step states. The chip is outlined and carries a lamp; it is
 * never filled with the lamp colour, and the label stays achromatic (DESIGN.md,
 * "Colour is illumination"). Every consumer of StatusBadge inherits that rule.
 */
export function StatusBadge({ status }: { status: string }) {
  return <StatusChip status={status} />;
}
