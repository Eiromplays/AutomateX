using AutomateX.Database;
using AutomateX.Plugin.Sdk;
using Microsoft.EntityFrameworkCore;

namespace AutomateX.Modules.Audit;

// Writes an audit row when an execution settles — automatic "who ran what, and did it succeed"
// coverage without touching each terminal site. Best-effort (event bus isolates a throw), singleton,
// so DB access goes through a scope. The actor is the run's trigger (cron/manual/webhook/…).
public sealed class AuditEventListener(IServiceScopeFactory scopeFactory) :
    IListenFor<ExecutionCompleted>,
    IListenFor<ExecutionFailed>
{
    public Task HandleAsync(ExecutionCompleted e, CancellationToken ct = default) =>
        RecordAsync(e.ExecutionId, e.WorkflowId, "execution.succeeded", ct);

    public Task HandleAsync(ExecutionFailed e, CancellationToken ct = default) =>
        RecordAsync(e.ExecutionId, e.WorkflowId, "execution.failed", ct);

    private async Task RecordAsync(Guid executionId, Guid workflowId, string action, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AutomateXDbContext>();

        var run = await dbContext.Executions
            .AsNoTracking()
            .Where(x => x.Id == executionId)
            .Select(x => new { x.WorkspaceId, x.TriggeredBy })
            .FirstOrDefaultAsync(ct);

        if (run is null)
        {
            return; // execution vanished (retention) — nothing to record
        }

        dbContext.AuditEntries.Add(AuditEntry.Create(
            run.TriggeredBy, run.WorkspaceId, action, "execution", executionId.ToString(), $"workflow {workflowId}"));
        await dbContext.SaveChangesAsync(ct);
    }
}
