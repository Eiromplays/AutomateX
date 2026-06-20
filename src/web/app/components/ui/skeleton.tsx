// Shimmer placeholders shown while a query loads, instead of a bare "Loading…".

export function Skeleton({ className }: { className?: string }) {
  return <div className={`animate-pulse rounded bg-zinc-800 ${className ?? ""}`} />;
}

// Placeholder rows inside the standard bordered list.
export function ListSkeleton({ rows = 4 }: { rows?: number }) {
  return (
    <ul className="divide-y divide-zinc-800 rounded-lg border border-zinc-800">
      {Array.from({ length: rows }).map((_, i) => (
        <li key={i} className="flex items-center justify-between px-4 py-3">
          <Skeleton className="h-4 w-48" />
          <Skeleton className="h-3 w-12" />
        </li>
      ))}
    </ul>
  );
}
