using AutomateX.Modules.Executions;
using Xunit;

namespace AutomateX.Tests;

// Dashboard aggregation rules, pinned on a pure calculator (no DB): status totals +
// success rate, a per-day series that spans the whole window (incl. empty days),
// duration percentiles, ranked top workflows with names + avg duration, and recent
// failures newest-first and capped.
public sealed class StatsCalculatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Wf1 = Guid.CreateVersion7();
    private static readonly Guid Wf2 = Guid.CreateVersion7();
    private static readonly Dictionary<Guid, string> Names = new() { [Wf1] = "Alpha", [Wf2] = "Beta" };

    private static ExecutionRow Row(ExecutionStatus status, double ageHours, double? durationMs = null, Guid? wf = null)
    {
        var started = Now.AddHours(-ageHours);
        DateTimeOffset? completed = durationMs is { } d ? started.AddMilliseconds(d) : null;
        return new ExecutionRow(Guid.CreateVersion7(), wf ?? Wf1, status, started, completed);
    }

    [Fact]
    public void Totals_and_success_rate()
    {
        ExecutionRow[] rows =
        [
            Row(ExecutionStatus.Succeeded, 1, 100),
            Row(ExecutionStatus.Succeeded, 2, 200),
            Row(ExecutionStatus.Succeeded, 3, 300),
            Row(ExecutionStatus.Failed, 4, 50),
            Row(ExecutionStatus.Running, 0),
        ];

        var stats = StatsCalculator.Compute(rows, Names, Now, days: 14);

        Assert.Equal(5, stats.Total);
        Assert.Equal(3, stats.Succeeded);
        Assert.Equal(1, stats.Failed);
        Assert.Equal(1, stats.Running);
        Assert.Equal(0.75, stats.SuccessRate); // 3 / (3 + 1), running excluded
    }

    [Fact]
    public void Success_rate_is_zero_without_terminal_runs()
    {
        ExecutionRow[] rows = [Row(ExecutionStatus.Running, 0)];

        Assert.Equal(0, StatsCalculator.Compute(rows, Names, Now, 14).SuccessRate);
    }

    [Fact]
    public void Per_day_buckets_span_the_window_including_empty_days()
    {
        ExecutionRow[] rows =
        [
            Row(ExecutionStatus.Succeeded, 1),   // today
            Row(ExecutionStatus.Failed, 2),      // today
            Row(ExecutionStatus.Succeeded, 26),  // ~yesterday
        ];

        var stats = StatsCalculator.Compute(rows, Names, Now, days: 14);

        Assert.Equal(14, stats.PerDay.Count);
        var today = stats.PerDay[^1];
        Assert.Equal("2026-06-10", today.Date);
        Assert.Equal(2, today.Total);
        Assert.Equal(1, today.Succeeded);
        Assert.Equal(1, today.Failed);
        Assert.Equal(1, stats.PerDay[^2].Total); // yesterday
        Assert.Equal(0, stats.PerDay[0].Total);  // 14 days ago = empty
    }

    [Fact]
    public void Duration_percentiles()
    {
        var rows = Enumerable.Range(1, 10)
            .Select(i => Row(ExecutionStatus.Succeeded, i, durationMs: i * 100))
            .ToArray();

        var stats = StatsCalculator.Compute(rows, Names, Now, 14);

        Assert.Equal(500, stats.P50DurationMs); // nearest-rank median of 100..1000
        Assert.Equal(1000, stats.P95DurationMs);
    }

    [Fact]
    public void Percentiles_are_null_when_no_durations()
    {
        ExecutionRow[] rows = [Row(ExecutionStatus.Running, 0)];

        var stats = StatsCalculator.Compute(rows, Names, Now, 14);

        Assert.Null(stats.P50DurationMs);
        Assert.Null(stats.P95DurationMs);
    }

    [Fact]
    public void Top_workflows_ranked_with_names_and_avg_duration()
    {
        ExecutionRow[] rows =
        [
            Row(ExecutionStatus.Succeeded, 1, 100, Wf1),
            Row(ExecutionStatus.Succeeded, 2, 300, Wf1),
            Row(ExecutionStatus.Failed, 3, 200, Wf1),
            Row(ExecutionStatus.Succeeded, 1, 500, Wf2),
        ];

        var stats = StatsCalculator.Compute(rows, Names, Now, 14);

        var top = stats.TopWorkflows[0];
        Assert.Equal("Alpha", top.Name);
        Assert.Equal(3, top.Total);
        Assert.Equal(2, top.Succeeded);
        Assert.Equal(1, top.Failed);
        Assert.Equal(200, top.AvgDurationMs); // (100 + 300 + 200) / 3
    }

    [Fact]
    public void Deleted_workflow_name_falls_back()
    {
        var unknown = Guid.CreateVersion7();
        ExecutionRow[] rows = [Row(ExecutionStatus.Succeeded, 1, 100, unknown)];

        Assert.Equal("(deleted)", StatsCalculator.Compute(rows, Names, Now, 14).TopWorkflows[0].Name);
    }

    [Fact]
    public void Recent_failures_are_newest_first_and_capped_at_five()
    {
        var rows = Enumerable.Range(1, 8)
            .Select(i => Row(ExecutionStatus.Failed, i)) // larger age = older
            .ToArray();

        var stats = StatsCalculator.Compute(rows, Names, Now, 14);

        Assert.Equal(5, stats.RecentFailures.Count);
        Assert.True(stats.RecentFailures[0].StartedAt > stats.RecentFailures[1].StartedAt);
    }
}
