using System.Globalization;

namespace AutomateX.Modules.Executions;

public sealed record ExecutionRow(
    Guid Id, Guid WorkflowId, ExecutionStatus Status, DateTimeOffset StartedAt, DateTimeOffset? CompletedAt);

public sealed record DayBucket(string Date, int Total, int Succeeded, int Failed);

public sealed record WorkflowStat(Guid WorkflowId, string Name, int Total, int Succeeded, int Failed, long? AvgDurationMs);

public sealed record RecentFailure(Guid Id, Guid WorkflowId, string Name, DateTimeOffset StartedAt);

public sealed record ExecutionStats(
    int Total,
    int Succeeded,
    int Failed,
    int Running,
    double SuccessRate,
    long? P50DurationMs,
    long? P95DurationMs,
    IReadOnlyList<DayBucket> PerDay,
    IReadOnlyList<WorkflowStat> TopWorkflows,
    IReadOnlyList<RecentFailure> RecentFailures);

// Pure dashboard aggregation over the raw execution rows in a window — no DB, so it's
// unit-tested directly. Success rate excludes Running (only terminal runs count);
// durations are terminal-only; the per-day series always spans the full window.
public static class StatsCalculator
{
    public static ExecutionStats Compute(
        IReadOnlyList<ExecutionRow> rows,
        IReadOnlyDictionary<Guid, string> workflowNames,
        DateTimeOffset now,
        int days)
    {
        var succeeded = rows.Count(r => r.Status == ExecutionStatus.Succeeded);
        var failed = rows.Count(r => r.Status == ExecutionStatus.Failed);
        var running = rows.Count(r => r.Status == ExecutionStatus.Running);
        var terminal = succeeded + failed;
        var successRate = terminal == 0 ? 0 : Math.Round((double)succeeded / terminal, 4);

        var durations = TerminalDurations(rows).OrderBy(ms => ms).ToList();

        var today = now.UtcDateTime.Date;
        var perDay = new List<DayBucket>(days);
        for (var offset = days - 1; offset >= 0; offset--)
        {
            var day = today.AddDays(-offset);
            var dayRows = rows.Where(r => r.StartedAt.UtcDateTime.Date == day).ToList();
            perDay.Add(new DayBucket(
                day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                dayRows.Count,
                dayRows.Count(r => r.Status == ExecutionStatus.Succeeded),
                dayRows.Count(r => r.Status == ExecutionStatus.Failed)));
        }

        var topWorkflows = rows
            .GroupBy(r => r.WorkflowId)
            .Select(g =>
            {
                var ds = TerminalDurations(g).ToList();
                return new WorkflowStat(
                    g.Key,
                    Name(workflowNames, g.Key),
                    g.Count(),
                    g.Count(r => r.Status == ExecutionStatus.Succeeded),
                    g.Count(r => r.Status == ExecutionStatus.Failed),
                    ds.Count == 0 ? null : (long)Math.Round(ds.Average()));
            })
            .OrderByDescending(w => w.Total)
            .ThenBy(w => w.Name, StringComparer.Ordinal)
            .Take(5)
            .ToList();

        var recentFailures = rows
            .Where(r => r.Status == ExecutionStatus.Failed)
            .OrderByDescending(r => r.StartedAt)
            .Take(5)
            .Select(r => new RecentFailure(r.Id, r.WorkflowId, Name(workflowNames, r.WorkflowId), r.StartedAt))
            .ToList();

        return new ExecutionStats(
            rows.Count, succeeded, failed, running, successRate,
            Percentile(durations, 0.50), Percentile(durations, 0.95),
            perDay, topWorkflows, recentFailures);
    }

    private static IEnumerable<double> TerminalDurations(IEnumerable<ExecutionRow> rows) =>
        rows
            .Where(r => r.CompletedAt is not null
                && r.Status is ExecutionStatus.Succeeded or ExecutionStatus.Failed)
            .Select(r => (r.CompletedAt!.Value - r.StartedAt).TotalMilliseconds)
            .Where(ms => ms >= 0);

    private static string Name(IReadOnlyDictionary<Guid, string> names, Guid id) =>
        names.TryGetValue(id, out var name) ? name : "(deleted)";

    // Nearest-rank percentile over an ascending-sorted list.
    private static long? Percentile(IReadOnlyList<double> sortedAscending, double p)
    {
        if (sortedAscending.Count == 0)
        {
            return null;
        }

        var rank = Math.Clamp((int)Math.Ceiling(p * sortedAscending.Count), 1, sortedAscending.Count);
        return (long)Math.Round(sortedAscending[rank - 1]);
    }
}
