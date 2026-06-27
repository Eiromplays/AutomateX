using AutomateX.Database;
using AutomateX.Modules.Executions;
using AutomateX.Plugin.Sdk;
using Microsoft.EntityFrameworkCore;

namespace AutomateX.Engine.Metrics;

// Translates best-effort engine events into OTel measurements. Singleton (like the SignalR listener),
// so DB access for execution duration goes through a scope. A throw here is isolated by the event bus
// and never affects an execution.
public sealed class MetricsEventListener(ExecutionMetrics metrics, IServiceScopeFactory scopeFactory) :
    IListenFor<ExecutionStarted>,
    IListenFor<StepCompleted>,
    IListenFor<StepFailed>,
    IListenFor<ExecutionCompleted>,
    IListenFor<ExecutionFailed>
{
    private static readonly string Succeeded = ExecutionStatus.Succeeded.ToString();
    private static readonly string Failed = ExecutionStatus.Failed.ToString();

    public Task HandleAsync(ExecutionStarted e, CancellationToken ct = default)
    {
        metrics.RecordExecutionStarted(e.TriggeredBy);
        return Task.CompletedTask;
    }

    public Task HandleAsync(StepCompleted e, CancellationToken ct = default)
    {
        metrics.RecordStep(e.ActionType, Succeeded);
        return Task.CompletedTask;
    }

    // StepFailed fires per attempt; only the terminal one (retries exhausted) is a settle.
    public Task HandleAsync(StepFailed e, CancellationToken ct = default)
    {
        if (!e.WillRetry)
        {
            metrics.RecordStep(e.ActionType, Failed);
        }

        return Task.CompletedTask;
    }

    public Task HandleAsync(ExecutionCompleted e, CancellationToken ct = default) =>
        SettleAsync(e.ExecutionId, Succeeded, ct);

    public Task HandleAsync(ExecutionFailed e, CancellationToken ct = default) =>
        SettleAsync(e.ExecutionId, Failed, ct);

    private async Task SettleAsync(Guid executionId, string status, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AutomateXDbContext>();

        var times = await dbContext.Executions
            .AsNoTracking()
            .Where(x => x.Id == executionId)
            .Select(x => new { x.StartedAt, x.CompletedAt })
            .FirstOrDefaultAsync(ct);

        if (times is null)
        {
            return; // execution vanished (retention) — count nothing rather than a bogus duration
        }

        var duration = (times.CompletedAt ?? DateTimeOffset.UtcNow) - times.StartedAt;
        metrics.RecordExecutionSettled(status, duration.TotalSeconds);
    }
}
