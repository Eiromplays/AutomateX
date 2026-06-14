using AutomateX.Database;
using AutomateX.Engine.Events;
using AutomateX.Modules.Executions;
using AutomateX.Plugin.Sdk;
using Microsoft.EntityFrameworkCore;

namespace AutomateX.Engine;

// EntryOrder lets a trigger start the run at a specific step instead of the first. null (and any
// out-of-range value) falls back to the first step by order — so existing fires are unchanged.
public sealed record RunWorkflow(Guid ExecutionId, Guid WorkflowId, string TriggeredBy, string? Payload = null, int? EntryOrder = null);

public static class RunWorkflowHandler
{
    public static async Task<object?> HandleAsync(
        RunWorkflow message,
        AutomateXDbContext dbContext,
        EngineEventBus eventBus,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (await dbContext.Executions.AnyAsync(x => x.Id == message.ExecutionId, cancellationToken))
        {
            // Redelivery — the execution already exists and is driven by ExecuteStep messages.
            return null;
        }

        var workspaceId = await dbContext.Workflows
            .Where(x => x.Id == message.WorkflowId)
            .Select(x => (Guid?)x.WorkspaceId)
            .FirstOrDefaultAsync(cancellationToken);

        var version = await dbContext.WorkflowVersions
            .AsNoTracking()
            .Include(x => x.Steps)
            .Where(x => x.WorkflowId == message.WorkflowId)
            .OrderByDescending(x => x.Version)
            .FirstOrDefaultAsync(cancellationToken);

        if (workspaceId is null || version is null)
        {
            logger.LogWarning("Workflow {WorkflowId} is missing or has no versions, skipping run", message.WorkflowId);
            return null;
        }

        var execution = Execution.Start(
            message.ExecutionId, message.WorkflowId, version.Id, message.TriggeredBy, message.Payload, workspaceId,
            version.ContinueOnFailure);
        dbContext.Executions.Add(execution);

        // Entry step: the trigger's chosen order if it exists, else the first by order. An invalid
        // order never throws — it degrades to the first step.
        var entryStep = (message.EntryOrder is { } order
            ? version.Steps.FirstOrDefault(x => x.Order == order)
            : null)
            ?? version.Steps.OrderBy(x => x.Order).FirstOrDefault();

        if (entryStep is null)
        {
            execution.Complete();
            await dbContext.SaveChangesAsync(cancellationToken);
            await eventBus.PublishAsync(new ExecutionStarted(execution.Id, execution.WorkflowId, message.TriggeredBy), cancellationToken);
            await eventBus.PublishAsync(new ExecutionCompleted(execution.Id, execution.WorkflowId), cancellationToken);
            return null;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await eventBus.PublishAsync(new ExecutionStarted(execution.Id, execution.WorkflowId, message.TriggeredBy), cancellationToken);
        return new ExecuteStep(execution.Id, entryStep.Order);
    }
}
