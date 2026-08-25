import type { ReactNode } from "react";
import { cn } from "~/lib/utils";

/*
 * The Rack Elevation primitives (see DESIGN.md).
 *
 * A list surface is a rack: a numbered rail down the left edge with equipment
 * seated against it in units. A unit is bounded by the rail and a hairline
 * rule — never by a box — which is what keeps containers from nesting.
 */

/** Reserved lamp meanings. Colour appears here and on focus rings, nowhere else. */
const lamp: Record<string, string> = {
  Succeeded: "bg-lamp-clear",
  Failed: "bg-lamp-fault",
  Running: "bg-lamp-active",
  Waiting: "bg-lamp-hold",
  Caught: "bg-lamp-hold",
  Pending: "bg-lamp-dark",
  Skipped: "bg-lamp-dark",
  // Plugin-host states. A deliberately switched-off plugin is an unlit lamp —
  // which is what an unlit lamp means on a rack panel.
  running: "bg-lamp-clear",
  exited: "bg-lamp-fault",
  disabled: "bg-lamp-dark",
  idle: "bg-lamp-dark",
};

/**
 * An 8px indicator in a fixed column, so state reads straight down the stack.
 * Square, not round: this is a panel lamp, not a dot.
 */
export function Lamp({ status, className }: { status: string; className?: string }) {
  return (
    <span
      className={cn("size-2 shrink-0 rounded-[1px]", lamp[status] ?? lamp.Pending, className)}
      // The lamp is decorative; the adjacent label already names the state.
      aria-hidden="true"
    />
  );
}

/** A status chip: lamp plus label, outlined. Never filled with the lamp colour. */
export function StatusChip({ status }: { status: string }) {
  return (
    <span className="inline-flex shrink-0 items-center gap-1.5 rounded-[2px] border border-engrave bg-panel-recess px-1.5 py-0.5">
      <Lamp status={status} />
      <span className="text-[11px] font-medium tracking-wide text-fg-body">{status}</span>
    </span>
  );
}

/** A printed panel label. */
export function Silkscreen({ children, className }: { children: ReactNode; className?: string }) {
  return (
    <span className={cn("text-[11px] font-semibold uppercase tracking-[0.1em] text-muted-foreground", className)}>
      {children}
    </span>
  );
}

/**
 * The rack: a rule running the full height at the rail line, with units seated
 * against it. `as` lets a caller keep semantic <ul>/<ol> where the list is one.
 */
export function Rack({
  children,
  empty,
  className,
}: {
  children: ReactNode;
  /** Rendered instead of the rail when the rack holds nothing. */
  empty?: ReactNode;
  className?: string;
}) {
  const bays = Array.isArray(children) ? children.flat().filter(Boolean) : children;
  if (empty !== undefined && (!bays || (Array.isArray(bays) && bays.length === 0))) {
    return <p className={cn("py-4 text-sm text-muted-foreground", className)}>{empty}</p>;
  }
  return (
    <div className={cn("relative", className)}>
      {/* the rail itself */}
      <div aria-hidden="true" className="pointer-events-none absolute inset-y-0 left-10 w-px bg-rail" />
      <ul>{bays}</ul>
    </div>
  );
}

/**
 * One unit of equipment. The rail numeral is presentational — it states the
 * unit's position in the rack, not the entity's identity.
 */
export function Unit({
  index,
  status,
  align = "center",
  children,
  className,
}: {
  index?: number;
  status?: string;
  /**
   * "center" suits a single-line unit. "start" is for a unit whose content
   * runs several lines: the rail cells then sit on the first line rather than
   * floating at the top edge of the whole block.
   */
  align?: "center" | "start";
  children: ReactNode;
  className?: string;
}) {
  const onFirstLine = align === "start" ? "pt-5" : "items-center";
  return (
    <li
      className={cn(
        "grid grid-cols-[2.5rem_auto_minmax(0,1fr)] gap-x-3 border-b border-engrave last:border-b-0",
        align === "start" ? "items-start" : "items-center",
        "bg-panel-face transition-colors hover:bg-panel-recess",
        className,
      )}
    >
      <span
        className={cn(
          "flex justify-end pr-3 font-mono text-[11px] leading-none tracking-[0.08em] text-rail-num",
          align === "start" ? "pt-5" : "items-center py-3",
        )}
      >
        {index === undefined ? "" : String(index).padStart(2, "0")}
      </span>
      <span className={cn("flex pl-3", onFirstLine, align === "center" && "py-3")}>
        {status ? <Lamp status={status} /> : null}
      </span>
      {children}
    </li>
  );
}
