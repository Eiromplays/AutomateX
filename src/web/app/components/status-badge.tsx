import { statusIcon } from "./icons";

const styles: Record<string, string> = {
  Succeeded: "bg-emerald-500/15 text-emerald-400",
  Failed: "bg-red-500/15 text-red-400",
  Running: "bg-amber-500/15 text-amber-400 animate-pulse",
  Waiting: "bg-sky-500/15 text-sky-400",
  Caught: "bg-orange-500/15 text-orange-400",
  Pending: "bg-zinc-500/15 text-zinc-400",
  Skipped: "bg-zinc-700/30 text-zinc-500",
};

export function StatusBadge({ status }: { status: string }) {
  const Icon = statusIcon[status] ?? statusIcon.Pending;
  return (
    <span
      className={`inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-xs font-medium ${styles[status] ?? styles.Pending}`}
    >
      <Icon className="h-3 w-3" aria-hidden="true" />
      {status}
    </span>
  );
}
