import { statusIcon } from "./icons";
import { Badge } from "./ui/badge";

// Semantic execution/step states. Rendered on the shadcn Badge base (rounded-full
// pill, size-3 icon, gap-1) with a per-status tint over a transparent border.
const styles: Record<string, string> = {
  Succeeded: "border-transparent bg-emerald-500/15 text-emerald-400",
  Failed: "border-transparent bg-red-500/15 text-red-400",
  Running: "border-transparent bg-amber-500/15 text-amber-400 animate-pulse",
  Waiting: "border-transparent bg-sky-500/15 text-sky-400",
  Caught: "border-transparent bg-orange-500/15 text-orange-400",
  Pending: "border-transparent bg-zinc-500/15 text-zinc-400",
  Skipped: "border-transparent bg-zinc-700/30 text-zinc-500",
};

export function StatusBadge({ status }: { status: string }) {
  const Icon = statusIcon[status] ?? statusIcon.Pending;
  return (
    <Badge variant="outline" className={styles[status] ?? styles.Pending}>
      <Icon aria-hidden="true" />
      {status}
    </Badge>
  );
}
