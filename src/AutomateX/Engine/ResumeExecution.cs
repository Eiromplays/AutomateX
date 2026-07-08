using System.Text.Json;
using System.Text.Json.Nodes;
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

    // A forEach child finished. Record its result in its own row (contention-free, idempotent via the
    // unique key), bump the counters atomically, launch the next item in this index's chain, and — for
    // the one child that brings the count to Total — complete the loop with the ordered results. Safe
    // under concurrent completions: the counter UPDATEs serialize on the row's write lock, and the
    // parent-status claim elects a single finisher.
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
        var result = payload ?? "null";
        var failed = ForEachState.ResultFailed(result);

        // Idempotent write of this item's slot. A redelivered resume conflicts on the unique key, affects
        // no row, and stops here — nothing double-counts.
        var inserted = await dbContext.Database.ExecuteSqlAsync(
            $"""
             INSERT INTO "ForEachItemResults" ("Id", "ExecutionId", "StepOrder", "ItemIndex", "ResultJson", "Failed")
             VALUES ({Guid.CreateVersion7()}, {execution.Id}, {state.StepOrder}, {itemIndex}, {result}::jsonb, {failed})
             ON CONFLICT ("ExecutionId", "StepOrder", "ItemIndex") DO NOTHING
             """,
            cancellationToken);
        if (inserted == 0)
        {
            return null;
        }

        await dbContext.ForEachStates
            .Where(x => x.Id == state.Id)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(x => x.CompletedCount, x => x.CompletedCount + 1)
                    .SetProperty(x => x.AnyFailed, x => x.AnyFailed || failed),
                cancellationToken);

        // This child's own post-increment count (its row lock is held to commit, so it reads its own
        // ordinal); exactly one child sees Total.
        var completed = await dbContext.ForEachStates
            .Where(x => x.Id == state.Id)
            .Select(x => x.CompletedCount)
            .SingleAsync(cancellationToken);

        if (completed < state.Total)
        {
            // Hold the cap: launch the next item in this index's chain, if any remain.
            var next = itemIndex + state.Concurrency;
            return next < state.Total
                ? new RunWorkflow(
                    Guid.CreateVersion7(), state.ChildWorkflowId, $"foreach:{execution.Id}", state.ItemPayload(next),
                    EntryOrder: null, ParentExecutionId: execution.Id, ParentStepOrder: state.StepOrder,
                    Depth: execution.Depth + 1, ParentItemIndex: next)
                : null;
        }

        // All items in — elect a single finisher via the parent-status claim, then complete the loop.
        var claimed = await dbContext.Executions
            .Where(x => x.Id == execution.Id && x.Status == ExecutionStatus.Waiting)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, ExecutionStatus.Running), cancellationToken);
        if (claimed == 0)
        {
            return null;
        }

        var slots = await dbContext.ForEachItemResults
            .Where(x => x.ExecutionId == execution.Id && x.StepOrder == state.StepOrder)
            .OrderBy(x => x.ItemIndex)
            .Select(x => x.ResultJson)
            .ToListAsync(cancellationToken);
        var array = new JsonArray();
        foreach (var slot in slots)
        {
            array.Add(JsonNode.Parse(slot));
        }

        var results = array.ToJsonString();
        step.Complete(results);
        dbContext.ForEachStates.Remove(state);
        await dbContext.ForEachItemResults
            .Where(x => x.ExecutionId == execution.Id && x.StepOrder == state.StepOrder)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        await eventBus.PublishAsync(new StepCompleted(execution.Id, step.StepOrder, ForEach.ActionType, results), cancellationToken);
        logger.LogInformation("Execution {ExecutionId} forEach complete ({Total} items)", execution.Id, state.Total);

        return await ExecuteStepHandler.AdvanceAsync(
            execution, step.StepOrder, ForEach.ActionType, results, dbContext, eventBus, options, logger, cancellationToken);
    }
}
