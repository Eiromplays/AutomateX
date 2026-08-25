import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { Link } from "react-router";
import { Lamp } from "../components/rack";
import { Skeleton } from "../components/ui/skeleton";
import { api, type ExecutionStats } from "../lib/api";

/**
 * One gauge on the readout strip. Not a card: the strip is divided by engraved
 * rules, so the four figures read as one instrument rather than four objects.
 */
function Readout({
  label,
  value,
  sub,
  hint,
}: {
  label: string;
  value: string;
  sub?: string;
  hint?: string;
}) {
  return (
    <div className="px-4 py-3 first:pl-0" title={hint}>
      <div className="text-[11px] font-semibold uppercase tracking-[0.1em] text-muted-foreground">
        {label}
      </div>
      <div className="mt-1 font-mono text-2xl text-fg-strong tabular-nums">{value}</div>
      {sub ? <div className="mt-0.5 text-sm text-muted-foreground">{sub}</div> : null}
    </div>
  );
}

function formatMs(value: number | null): string {
  if (value === null) return "—";
  if (value < 1000) return `${value}ms`;
  return `${(value / 1000).toFixed(value >= 10000 ? 0 : 1)}s`;
}

function DayChart({ perDay }: { perDay: ExecutionStats["perDay"] }) {
  const max = Math.max(1, ...perDay.map((d) => d.total));
  const chartH = 100;
  const barW = 100 / perDay.length;
  const pad = barW * 0.16;

  return (
    <svg
      viewBox="0 0 100 100"
      preserveAspectRatio="none"
      className="h-32 w-full"
      role="img"
      aria-label="Executions per day"
    >
      {perDay.map((d, i) => {
        const totalH = (d.total / max) * chartH;
        const failH = (d.failed / max) * chartH;
        const x = i * barW + pad;
        const w = barW - pad * 2;
        return (
          <g key={d.date}>
            <title>{`${d.date}: ${d.succeeded} ok, ${d.failed} failed`}</title>
            <rect
              x={x}
              y={chartH - totalH}
              width={w}
              height={Math.max(0, totalH - failH)}
              fill="var(--lamp-clear)"
            />
            <rect x={x} y={chartH - failH} width={w} height={failH} fill="var(--lamp-fault)" />
          </g>
        );
      })}
    </svg>
  );
}

export default function Dashboard() {
  const [days, setDays] = useState(14);
  const { data, isLoading } = useQuery({
    queryKey: ["stats", days],
    queryFn: () => api.stats.get(days),
  });
  const { data: workflows } = useQuery({
    queryKey: ["workflows"],
    queryFn: api.workflows.list,
  });

  if (isLoading) {
    return (
      <div className="space-y-6">
        <Skeleton className="h-7 w-40" />
        <div className="grid grid-cols-2 gap-3 md:grid-cols-4">
          {Array.from({ length: 4 }).map((_, i) => (
            <Skeleton key={i} className="h-[88px]" />
          ))}
        </div>
        <Skeleton className="h-40" />
        <div className="grid gap-6 md:grid-cols-2">
          <Skeleton className="h-48" />
          <Skeleton className="h-48" />
        </div>
      </div>
    );
  }
  if (!data) return <div className="text-sm text-muted-foreground">No data.</div>;

  const statByWorkflow = new Map(data.topWorkflows.map((w) => [w.workflowId, w]));
  const sortedWorkflows = [...(workflows ?? [])].sort((a, b) => {
    const ra = statByWorkflow.get(a.id)?.total ?? -1;
    const rb = statByWorkflow.get(b.id)?.total ?? -1;
    return rb - ra || a.name.localeCompare(b.name);
  });

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold tracking-[-0.01em]">Dashboard</h1>
        <select
          value={days}
          onChange={(e) => setDays(Number(e.target.value))}
          className="rounded-[2px] border border-line-strong bg-card px-2.5 py-1 text-xs text-fg-body focus:border-brand-bright focus:outline-none"
        >
          <option value={7}>Last 7 days</option>
          <option value={14}>Last 14 days</option>
          <option value={30}>Last 30 days</option>
          <option value={90}>Last 90 days</option>
        </select>
      </div>

      <div className="grid grid-cols-2 divide-x divide-engrave border-y border-engrave md:grid-cols-4">
        <Readout label="Executions" value={String(data.total)} sub={`${data.running} running`} />
        <Readout
          label="Success rate"
          value={`${Math.round(data.successRate * 100)}%`}
          sub={`${data.succeeded} ok · ${data.failed} failed`}
          hint="Succeeded divided by terminal runs (still-running are excluded)."
        />
        <Readout
          label="Duration p50"
          value={formatMs(data.p50DurationMs)}
          sub="median run"
          hint="Half of all runs finish faster than this — the typical case."
        />
        <Readout
          label="Duration p95"
          value={formatMs(data.p95DurationMs)}
          sub="slowest 5%"
          hint="95% of runs finish faster than this — the slow tail."
        />
      </div>

      <div className="border-t border-engrave pt-4">
        <div className="mb-2 flex items-center justify-between text-xs text-muted-foreground">
          <span className="text-[11px] font-semibold uppercase tracking-[0.1em]">Executions per day</span>
          <span className="flex gap-3">
            <span className="flex items-center gap-1">
              <Lamp status="Succeeded" /> succeeded
            </span>
            <span className="flex items-center gap-1">
              <Lamp status="Failed" /> failed
            </span>
          </span>
        </div>
        {data.total === 0 ? (
          <div className="py-8 text-center text-sm text-muted-foreground">No executions yet in this window.</div>
        ) : (
          <DayChart perDay={data.perDay} />
        )}
      </div>

      <div className="grid gap-6 md:grid-cols-2">
        <div className="border-t border-engrave pt-4">
          <div className="mb-3 flex items-center justify-between">
            <span className="text-lg font-medium text-fg-strong">Workflows</span>
            <Link to="/workflows/new" className="text-xs text-brand hover:underline">
              + New
            </Link>
          </div>
          {sortedWorkflows.length === 0 ? (
            <div className="text-sm text-muted-foreground">
              No workflows yet —{" "}
              <Link to="/workflows/new" className="text-brand hover:underline">
                create one
              </Link>
              .
            </div>
          ) : (
            <ul className="space-y-2">
              {sortedWorkflows.map((w) => {
                const s = statByWorkflow.get(w.id);
                return (
                  <li key={w.id} className="flex items-center justify-between gap-3 text-sm">
                    <Link to={`/workflows/${w.id}`} className="truncate text-fg-emphasis hover:text-white">
                      {w.name}
                    </Link>
                    {s ? (
                      <span className="flex shrink-0 items-center gap-3 text-xs text-muted-foreground">
                        <span>{s.total} runs</span>
                        <span className="inline-flex items-center gap-1.5 text-muted-foreground">
                          <Lamp status="Succeeded" />
                          {s.succeeded}
                        </span>
                        {s.failed > 0 ? (
                          <span className="inline-flex items-center gap-1.5 text-muted-foreground">
                            <Lamp status="Failed" />
                            {s.failed}
                          </span>
                        ) : null}
                        <span>{formatMs(s.avgDurationMs)}</span>
                      </span>
                    ) : (
                      <span className="shrink-0 text-xs text-muted-foreground">no recent runs</span>
                    )}
                  </li>
                );
              })}
            </ul>
          )}
        </div>

        <div className="border-t border-engrave pt-4">
          <div className="mb-3 text-lg font-medium text-fg-strong">Recent failures</div>
          {data.recentFailures.length === 0 ? (
            <div className="text-sm text-muted-foreground">None in this window.</div>
          ) : (
            <ul className="space-y-2">
              {data.recentFailures.map((f) => (
                <li key={f.id} className="flex items-center justify-between gap-3 text-sm">
                  <Link to={`/executions/${f.id}`} className="truncate text-fg-emphasis hover:text-white">
                    {f.name}
                  </Link>
                  <span className="shrink-0 text-xs text-muted-foreground">
                    {new Date(f.startedAt).toLocaleString()}
                  </span>
                </li>
              ))}
            </ul>
          )}
        </div>
      </div>
    </div>
  );
}
