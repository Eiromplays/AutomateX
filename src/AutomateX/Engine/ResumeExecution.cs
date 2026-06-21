using System.Text.Json;
using AutomateX.Database;
using AutomateX.Engine.Actions;
using AutomateX.Engine.Events;
using AutomateX.Modules.Executions;
using AutomateX.Plugin.Sdk;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AutomateX.Engine;

// Wakes a suspended execution at a wait step — from the scheduled timer/timeout or an external
// signal (the resume API). Reason is "timer"/"timeout"/"resumed"; Payload (JSON) becomes the wait
// step's output so a downstream gate/switch can branch on it.
public sealed record ResumeExecution(Guid ExecutionId, int StepOrder, string Reason, string? Payload);

public static class ResumeExecutionHandler
{
    public static async Task<object?> HandleAsync(
        ResumeExecution message,
        AutomateXDbContext dbContext,
        EngineEventBus eventBus,
        IOptions<EngineOptions> engineOptions,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var execution = await dbContext.Executions
            .Include(x => x.Steps)
            .FirstOrDefaultAsync(x => x.Id == message.ExecutionId, cancellationToken);

        if (execution is null || execution.Status is not ExecutionStatus.Waiting)
        {
            return null; // already resumed/terminal — idempotent
        }

        var waitStep = execution.Steps.FirstOrDefault(x => x.StepOrder == message.StepOrder);
        if (waitStep is null || waitStep.Status is not ExecutionStatus.Waiting)
        {
            return null;
        }

        // Atomic claim: only one resume (timer vs signal race) wins the Waiting → Running transition.
        var claimed = await dbContext.Executions
            .Where(x => x.Id == message.ExecutionId && x.Status == ExecutionStatus.Waiting)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, ExecutionStatus.Running), cancellationToken);

        if (claimed == 0)
        {
            return null; // lost the race
        }

        var output = message.Payload ?? JsonSerializer.Serialize(new { reason = message.Reason });
        waitStep.Complete(output);
        await dbContext.SaveChangesAsync(cancellationToken);

        await eventBus.PublishAsync(
            new StepCompleted(execution.Id, message.StepOrder, Wait.ActionType, output), cancellationToken);
        logger.LogInformation(
            "Execution {ExecutionId} resumed at step {StepOrder} ({Reason})", execution.Id, message.StepOrder, message.Reason);

        return await ExecuteStepHandler.AdvanceAsync(
            execution, message.StepOrder, Wait.ActionType, output,
            dbContext, eventBus, engineOptions.Value, logger, cancellationToken);
    }
}
