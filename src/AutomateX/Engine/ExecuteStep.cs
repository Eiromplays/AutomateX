using AutomateX.Database;
using AutomateX.Engine.Actions;
using AutomateX.Modules.Executions;
using Microsoft.EntityFrameworkCore;
using Wolverine;

namespace AutomateX.Engine;

public sealed record ExecuteStep(Guid ExecutionId, int StepOrder);

public static class ExecuteStepHandler
{
    private const int MaxAttempts = 4;

    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(2),
    ];

    public static async Task<object?> HandleAsync(
        ExecuteStep message,
        AutomateXDbContext dbContext,
        ActionRegistry actions,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var execution = await dbContext.Executions
            .Include(x => x.Steps)
            .FirstOrDefaultAsync(x => x.Id == message.ExecutionId, cancellationToken);

        if (execution is null || execution.Status is not ExecutionStatus.Running)
        {
            return null;
        }

        var step = await dbContext.WorkflowSteps
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.WorkflowVersionId == execution.WorkflowVersionId && x.Order == message.StepOrder,
                cancellationToken);

        if (step is null)
        {
            execution.Fail();
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogError(
                "Step {StepOrder} not found for execution {ExecutionId}, marking failed",
                message.StepOrder, message.ExecutionId);
            return null;
        }

        var stepExecution = execution.Steps.FirstOrDefault(x => x.StepOrder == message.StepOrder);
        if (stepExecution is { Status: ExecutionStatus.Succeeded })
        {
            // Redelivered after a crash between step completion and the cascade — just advance.
            return await NextStepOrCompleteAsync(execution, message.StepOrder, dbContext, cancellationToken);
        }

        if (stepExecution is null)
        {
            stepExecution = execution.AddStep(step.ActionType, message.StepOrder);
            // Explicit Add: with client-set keys, entities discovered via a tracked parent's
            // navigation are assumed to exist (Modified), which saves as an UPDATE hitting 0 rows.
            dbContext.StepExecutions.Add(stepExecution);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        try
        {
            var output = await actions.Get(step.ActionType).ExecuteAsync(step.ConfigJson, cancellationToken);
            stepExecution.Complete(output);
        }
        catch (Exception ex)
        {
            stepExecution.RecordFailure(ex.Message);

            if (stepExecution.Attempts >= MaxAttempts)
            {
                stepExecution.Fail(ex.Message);
                execution.Fail();
                await dbContext.SaveChangesAsync(cancellationToken);
                logger.LogError(ex,
                    "Execution {ExecutionId} failed at step {StepOrder} after {Attempts} attempts",
                    execution.Id, message.StepOrder, stepExecution.Attempts);
                return null;
            }

            await dbContext.SaveChangesAsync(cancellationToken);

            var delay = RetryDelays[Math.Min(stepExecution.Attempts - 1, RetryDelays.Length - 1)];
            logger.LogWarning(
                "Step {StepOrder} of execution {ExecutionId} failed (attempt {Attempts}/{MaxAttempts}), retrying in {Delay}: {Error}",
                message.StepOrder, execution.Id, stepExecution.Attempts, MaxAttempts, delay, ex.Message);

            return new ExecuteStep(message.ExecutionId, message.StepOrder).DelayedFor(delay);
        }

        return await NextStepOrCompleteAsync(execution, message.StepOrder, dbContext, cancellationToken);
    }

    private static async Task<object?> NextStepOrCompleteAsync(
        Execution execution,
        int currentOrder,
        AutomateXDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var nextOrder = await dbContext.WorkflowSteps
            .Where(x => x.WorkflowVersionId == execution.WorkflowVersionId && x.Order > currentOrder)
            .OrderBy(x => x.Order)
            .Select(x => (int?)x.Order)
            .FirstOrDefaultAsync(cancellationToken);

        if (nextOrder is null)
        {
            execution.Complete();
            await dbContext.SaveChangesAsync(cancellationToken);
            return null;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return new ExecuteStep(execution.Id, nextOrder.Value);
    }
}
