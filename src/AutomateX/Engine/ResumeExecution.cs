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
public sealed record ResumeExecution(Guid ExecutionId, int StepOrder, string Reason, string? Payload, int? ItemIndex = null);

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

        var step = execution.Steps.FirstOrDefault(x => x.StepOrder == message.StepOrder);
        if (step is null || step.Status is not ExecutionStatus.Waiting)
        {
            return null;
        }

        // forEach: accumulate this item's result; the loop step completes only once every item is in.
        if (message.ItemIndex is { } itemIndex)
        {
            var state = await dbContext.ForEachStates
                .FirstOrDefaultAsync(x => x.ExecutionId == message.ExecutionId && x.StepOrder == message.StepOrder, cancellationToken);
            if (state is not null)
            {
                return await AccumulateForEachAsync(
                    execution, step, state, itemIndex, message.Payload, dbContext, eventBus, engineOptions.Value, logger, cancellationToken);
            }
        }

        // wait / workflow.call: atomic claim (timer vs signal race), complete the step, advance.
        var claimed = await dbContext.Executions
            .Where(x => x.Id == message.ExecutionId && x.Status == ExecutionStatus.Waiting)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, ExecutionStatus.Running), cancellationToken);

        if (claimed == 0)
        {
            return null; // lost the race
        }

        var output = message.Payload ?? JsonSerializer.Serialize(new { reason = message.Reason });
        step.Complete(output);
        await dbContext.SaveChangesAsync(cancellationToken);

        await eventBus.PublishAsync(
            new StepCompleted(execution.Id, message.StepOrder, step.ActionType, output), cancellationToken);
        logger.LogInformation(
            "Execution {ExecutionId} resumed at step {StepOrder} ({Reason})", execution.Id, message.StepOrder, message.Reason);

        return await ExecuteStepHandler.AdvanceAsync(
            execution, message.StepOrder, step.ActionType, output,
            dbContext, eventBus, engineOptions.Value, logger, cancellationToken);
    }

    // A forEach child finished: record its result, then either launch the next item (sequential) or,
    // once all items are in, complete the forEach step with the ordered results and advance.
    private static async Task<object?> AccumulateForEachAsync(
        Execution execution,
        StepExecution step,
        ForEachState state,
        int itemIndex,
        string? payload,
        AutomateXDbContext dbContext,
        EngineEventBus eventBus,
        EngineOptions options,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (state.IsFilled(itemIndex))
        {
            return null; // redelivered child-resume — already counted
        }

        var result = payload ?? "null";
        state.Record(itemIndex, result, ForEachState.ResultFailed(result));

        if (!state.IsComplete)
        {
            if (state.NextIndex < state.Total)
            {
                var nextIndex = state.NextIndex;
                var nextPayload = state.ItemPayload(nextIndex);
                state.TakeNext();
                await dbContext.SaveChangesAsync(cancellationToken);
                return new RunWorkflow(
                    Guid.CreateVersion7(), state.ChildWorkflowId, $"foreach:{execution.Id}", nextPayload,
                    EntryOrder: null, ParentExecutionId: execution.Id, ParentStepOrder: state.StepOrder,
                    Depth: execution.Depth + 1, ParentItemIndex: nextIndex);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            return null;
        }

        // All items done — claim the parent and complete the loop with the ordered results.
        var claimed = await dbContext.Executions
            .Where(x => x.Id == execution.Id && x.Status == ExecutionStatus.Waiting)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, ExecutionStatus.Running), cancellationToken);

        if (claimed == 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return null;
        }

        var results = state.ResultsJson;
        step.Complete(results);
        dbContext.ForEachStates.Remove(state);
        await dbContext.SaveChangesAsync(cancellationToken);

        await eventBus.PublishAsync(new StepCompleted(execution.Id, step.StepOrder, ForEach.ActionType, results), cancellationToken);
        logger.LogInformation("Execution {ExecutionId} forEach complete ({Total} items)", execution.Id, state.Total);

        return await ExecuteStepHandler.AdvanceAsync(
            execution, step.StepOrder, ForEach.ActionType, results, dbContext, eventBus, options, logger, cancellationToken);
    }
}
