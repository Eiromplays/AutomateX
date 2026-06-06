using AutomateX.Database;
using AutomateX.Modules.Executions;
using Microsoft.EntityFrameworkCore;

namespace AutomateX.Engine;

public sealed record RunWorkflow(Guid ExecutionId, Guid WorkflowId, string TriggeredBy);

public static class RunWorkflowHandler
{
    public static async Task<object?> HandleAsync(
        RunWorkflow message,
        AutomateXDbContext dbContext,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (await dbContext.Executions.AnyAsync(x => x.Id == message.ExecutionId, cancellationToken))
        {
            // Redelivery — the execution already exists and is driven by ExecuteStep messages.
            return null;
        }

        var version = await dbContext.WorkflowVersions
            .AsNoTracking()
            .Include(x => x.Steps)
            .Where(x => x.WorkflowId == message.WorkflowId)
            .OrderByDescending(x => x.Version)
            .FirstOrDefaultAsync(cancellationToken);

        if (version is null)
        {
            logger.LogWarning("Workflow {WorkflowId} has no versions, skipping run", message.WorkflowId);
            return null;
        }

        var execution = Execution.Start(message.ExecutionId, message.WorkflowId, version.Id, message.TriggeredBy);
        dbContext.Executions.Add(execution);

        var firstStep = version.Steps.OrderBy(x => x.Order).FirstOrDefault();
        if (firstStep is null)
        {
            execution.Complete();
            await dbContext.SaveChangesAsync(cancellationToken);
            return null;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return new ExecuteStep(execution.Id, firstStep.Order);
    }
}
