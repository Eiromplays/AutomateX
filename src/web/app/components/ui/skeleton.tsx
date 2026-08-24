import { cn } from "~/lib/utils";
import { Rack, Unit } from "../rack";

// Shimmer placeholders shown while a query loads, instead of a bare "Loading…".
export function Skeleton({ className }: { className?: string }) {
  return <div data-slot="skeleton" className={cn("animate-pulse rounded-[2px] bg-accent", className)} />;
}

// Placeholder units on the rack, so loading has the same shape as loaded.
export function ListSkeleton({ rows = 4 }: { rows?: number }) {
  return (
    <Rack>
      {Array.from({ length: rows }).map((_, i) => (
        <Unit key={i}>
          <div className="flex items-center justify-between py-3 pr-3">
            <Skeleton className="h-4 w-48" />
            <Skeleton className="h-3 w-12" />
          </div>
        </Unit>
      ))}
    </Rack>
  );
}
