import {
  CheckCircle2,
  Circle,
  CircleDot,
  CircleSlash,
  Clock,
  type LucideIcon,
  MinusCircle,
  RotateCw,
  ShieldCheck,
  XCircle,
} from "lucide-react";

// v4.9 Track B: one tree-shakeable icon set for the domains that used ad-hoc glyphs. Keeps the mapping
// from a status/state string to its icon in one place so the executions and plugins surfaces stay
// consistent. Semantic one-off icons (nav, refs, remove/add) are imported from lucide-react directly at
// their call sites.

// Execution + step status chips (StatusBadge). Keys are the server status strings.
export const statusIcon: Record<string, LucideIcon> = {
  Succeeded: CheckCircle2,
  Failed: XCircle,
  Running: RotateCw,
  Waiting: Clock,
  Caught: ShieldCheck,
  Pending: CircleDot,
  Skipped: MinusCircle,
};

// Plugin host state chips (PluginOps). Keys are the states from stateStyle().
export const pluginStateIcon: Record<string, LucideIcon> = {
  running: CircleDot,
  exited: XCircle,
  disabled: CircleSlash,
  idle: Circle,
};
