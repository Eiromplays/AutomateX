using AutomateX.Database;
using AutomateX.Engine.Events;
using AutomateX.Modules.Executions;
using AutomateX.Modules.Variables;
using AutomateX.Plugin.Sdk;
using Microsoft.EntityFrameworkCore;

namespace AutomateX.Engine;

// EntryOrder lets a trigger start the run at a specific step instead of the first. null (and any
// out-of-range value) falls back to the first step by order — so existing fires are unchanged.
public sealed record RunWorkflow(
    Guid ExecutionId,
    Guid WorkflowId,
    string TriggeredBy,
    string? Payload = null,
    int? EntryOrder = null,
    // Set when this run is a sub-workflow call — the parent to resume when it finishes.
    Guid? ParentExecutionId = null,
    int? ParentStepOrder = null,
    int Depth = 0,
    int? ParentItemIndex = null,
    // Per-run environment override (by name) for {{vars.x}} resolution; null inherits the workspace's
    // active environment, then 'default'.
    string? Environment = null);

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

        var workflow = await dbContext.Workflows
            .Where(x => x.Id == message.WorkflowId)
            .Select(x => new { x.WorkspaceId, x.Enabled })
            .FirstOrDefaultAsync(cancellationToken);

        var version = await dbContext.WorkflowVersions
            .AsNoTracking()
            .Include(x => x.Steps)
            .Where(x => x.WorkflowId == message.WorkflowId)
            .OrderByDescending(x => x.Version)
            .FirstOrDefaultAsync(cancellationToken);

        if (workflow is null || version is null)
        {
            logger.LogWarning("Workflow {WorkflowId} is missing or has no versions, skipping run", message.WorkflowId);
            return null;
        }

        // Disabled = paused at the source: drop the run regardless of how it was triggered.
        if (!workflow.Enabled)
        {
            logger.LogInformation("Workflow {WorkflowId} is disabled — skipping run", message.WorkflowId);
            return null;
        }

        var workspaceId = workflow.WorkspaceId;
        var environmentId = await ResolveEnvironmentAsync(dbContext, workspaceId, message.Environment, cancellationToken);

        var execution = Execution.Start(
            message.ExecutionId, message.WorkflowId, version.Id, message.TriggeredBy, message.Payload, workspaceId,
            version.ContinueOnFailure, message.ParentExecutionId, message.ParentStepOrder, message.Depth,
            message.ParentItemIndex, environmentId);
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

    // Pick the environment this run resolves {{vars.x}} against: a per-run override by name, else the
    // workspace's active environment, else 'default' (else the first). Null when none are configured.
    private static async Task<Guid?> ResolveEnvironmentAsync(
        AutomateXDbContext dbContext, Guid workspaceId, string? overrideName, CancellationToken cancellationToken)
    {
        var environments = await dbContext.WorkspaceEnvironments
            .AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId)
            .Select(x => new { x.Id, x.Name })
            .ToListAsync(cancellationToken);

        if (environments.Count == 0)
        {
            return null;
        }

        if (overrideName is { Length: > 0 }
            && environments.FirstOrDefault(e => e.Name == overrideName) is { } match)
        {
            return match.Id;
        }

        var active = await dbContext.Workspaces
            .AsNoTracking()
            .Where(x => x.Id == workspaceId)
            .Select(x => x.ActiveEnvironmentId)
            .FirstOrDefaultAsync(cancellationToken);
        if (active is { } activeId && environments.Any(e => e.Id == activeId))
        {
            return activeId;
        }

        return environments.FirstOrDefault(e => e.Name == WorkspaceEnvironment.DefaultName)?.Id ?? environments[0].Id;
    }
}
