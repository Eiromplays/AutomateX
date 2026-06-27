using System.Diagnostics.Metrics;
using AutomateX.Engine.Metrics;
using Xunit;

namespace AutomateX.Tests;

// Locks the recording contract: instrument names, values, and bounded tags. The event→metric glue
// and duration lookup in MetricsEventListener are thin; this covers the measurable surface.
public sealed class ExecutionMetricsTests
{
    private sealed record Measurement(string Instrument, double Value, IReadOnlyDictionary<string, string?> Tags);

    private static (ExecutionMetrics Metrics, List<Measurement> Captured) Listen()
    {
        var metrics = new ExecutionMetrics();
        var captured = new List<Measurement>();
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == ExecutionMetrics.MeterName)
                {
                    l.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>(
            (instrument, value, tags, _) => captured.Add(new(instrument.Name, value, ToDict(tags))));
        listener.SetMeasurementEventCallback<double>(
            (instrument, value, tags, _) => captured.Add(new(instrument.Name, value, ToDict(tags))));
        listener.Start();
        return (metrics, captured);
    }

    private static Dictionary<string, string?> ToDict(ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var dict = new Dictionary<string, string?>();
        foreach (var tag in tags)
        {
            dict[tag.Key] = tag.Value?.ToString();
        }

        return dict;
    }

    [Fact]
    public void Records_started_tagged_by_trigger()
    {
        var (metrics, captured) = Listen();

        metrics.RecordExecutionStarted("cron");

        var m = Assert.Single(captured, x => x.Instrument == "automatex.executions.started");
        Assert.Equal(1, m.Value);
        Assert.Equal("cron", m.Tags["trigger"]);
    }

    [Fact]
    public void Records_settled_counter_and_duration_tagged_by_status()
    {
        var (metrics, captured) = Listen();

        metrics.RecordExecutionSettled("Failed", 1.5);

        var settled = Assert.Single(captured, x => x.Instrument == "automatex.executions.settled");
        Assert.Equal(1, settled.Value);
        Assert.Equal("Failed", settled.Tags["status"]);

        var duration = Assert.Single(captured, x => x.Instrument == "automatex.execution.duration");
        Assert.Equal(1.5, duration.Value);
        Assert.Equal("Failed", duration.Tags["status"]);
    }

    [Fact]
    public void Records_step_tagged_by_action_and_status()
    {
        var (metrics, captured) = Listen();

        metrics.RecordStep("ssh.command", "Succeeded");

        var m = Assert.Single(captured, x => x.Instrument == "automatex.steps.settled");
        Assert.Equal(1, m.Value);
        Assert.Equal("ssh.command", m.Tags["action"]);
        Assert.Equal("Succeeded", m.Tags["status"]);
    }
}
