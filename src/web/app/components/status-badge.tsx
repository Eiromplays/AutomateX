const styles: Record<string, string> = {
  Succeeded: "bg-emerald-500/15 text-emerald-400",
  Failed: "bg-red-500/15 text-red-400",
  Running: "bg-amber-500/15 text-amber-400 animate-pulse",
  Pending: "bg-zinc-500/15 text-zinc-400",
};

export function StatusBadge({ status }: { status: string }) {
  return (
    <span
      className={`inline-flex rounded-full px-2 py-0.5 text-xs font-medium ${styles[status] ?? styles.Pending}`}
    >
      {status}
    </span>
  );
}
