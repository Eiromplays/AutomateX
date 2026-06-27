using System.Diagnostics.Metrics;

namespace AutomateX.Engine.Metrics;

// Owns the AutomateX OTel Meter + its instruments. Kept free of engine/DB dependencies so the
// recording logic is unit-testable with a MeterListener; the event→metric glue (and the duration
// lookup) lives in MetricsEventListener. Tags are bounded enums/type-strings only — never ids —
// to keep Prometheus cardinality flat.
public sealed class ExecutionMetrics : IDisposable
{
    // Synced with ServiceDefaults' metrics.AddMeter("AutomateX").
    public const string MeterName = "AutomateX";

    private readonly Meter _meter = new(MeterName);
    private readonly Counter<long> _executionsStarted;
    private readonly Counter<long> _executionsSettled;
    private readonly Histogram<double> _executionDuration;
    private readonly Counter<long> _stepsSettled;

    public ExecutionMetrics()
    {
        _executionsStarted = _meter.CreateCounter<long>(
            "automatex.executions.started", unit: "{execution}", description: "Executions started.");
        _executionsSettled = _meter.CreateCounter<long>(
            "automatex.executions.settled", unit: "{execution}", description: "Executions reaching a terminal state.");
        _executionDuration = _meter.CreateHistogram<double>(
            "automatex.execution.duration", unit: "s", description: "Wall-clock duration of a settled execution.");
        _stepsSettled = _meter.CreateCounter<long>(
            "automatex.steps.settled", unit: "{step}", description: "Steps reaching a terminal state.");
    }

    public void RecordExecutionStarted(string trigger) =>
        _executionsStarted.Add(1, new KeyValuePair<string, object?>("trigger", trigger));

    public void RecordExecutionSettled(string status, double durationSeconds)
    {
        var statusTag = new KeyValuePair<string, object?>("status", status);
        _executionsSettled.Add(1, statusTag);
        _executionDuration.Record(durationSeconds, statusTag);
    }

    public void RecordStep(string action, string status) =>
        _stepsSettled.Add(
            1,
            new KeyValuePair<string, object?>("action", action),
            new KeyValuePair<string, object?>("status", status));

    public void Dispose() => _meter.Dispose();
}
