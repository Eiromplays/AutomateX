using AutomateX.Database;
using AutomateX.Engine.Events;
using AutomateX.Modules.Executions;
using AutomateX.Plugin.Sdk;
using Microsoft.EntityFrameworkCore;

namespace AutomateX.Engine;

// Re-run from a chosen step, reusing the source run's upstream outputs. Unlike a full retry (which
// replays on the latest version), this pins to the source's version so the seeded step outputs line
// up by order.
public sealed record RetryFromStep(Guid ExecutionId, Guid SourceExecutionId, int FromOrder);

public static class RetryFromStepHandler
{
    public static async Task<object?> HandleAsync(
        RetryFromStep message,
        AutomateXDbContext dbContext,
        EngineEventBus eventBus,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (await dbContext.Executions.AnyAsync(x => x.Id == message.ExecutionId, cancellationToken))
        {
            return null; // redelivery
        }

        var source = await dbContext.Executions
            .AsNoTracking()
            .Include(x => x.Steps)
            .FirstOrDefaultAsync(x => x.Id == message.SourceExecutionId, cancellationToken);

        var version = source is null
            ? null
            : await dbContext.WorkflowVersions
                .AsNoTracking()
                .Include(x => x.Steps)
                .FirstOrDefaultAsync(x => x.Id == source.WorkflowVersionId, cancellationToken);

        if (source is null || version is null)
        {
            logger.LogWarning("retry-from: source execution {SourceId} or its version is missing", message.SourceExecutionId);
            return null;
        }

        var entryStep = version.Steps.FirstOrDefault(x => x.Order == message.FromOrder);
        if (entryStep is null)
        {
            logger.LogWarning("retry-from: step {Order} is out of range for the source version", message.FromOrder);
            return null;
        }

        var execution = Execution.Start(
            message.ExecutionId, source.WorkflowId, source.WorkflowVersionId, $"retry-from:{source.Id}",
            source.TriggerPayload, source.WorkspaceId, source.ContinueOnFailure);
        dbContext.Executions.Add(execution);

        // Seed the upstream steps' results so {{steps.…}} and join readiness see the prior state.
        foreach (var step in source.Steps.Where(s => s.StepOrder < message.FromOrder))
        {
            dbContext.StepExecutions.Add(
                StepExecution.Seed(execution.Id, step.StepOrder, step.ActionType, step.Status, step.Output));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await eventBus.PublishAsync(
            new ExecutionStarted(execution.Id, execution.WorkflowId, execution.TriggeredBy), cancellationToken);
        return new ExecuteStep(execution.Id, message.FromOrder);
    }
}
