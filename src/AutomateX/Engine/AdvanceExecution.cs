using AutomateX.Database;
using AutomateX.Engine.Actions;
using AutomateX.Engine.Events;
using AutomateX.Modules.Executions;
using AutomateX.Plugin.Sdk;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Wolverine;

namespace AutomateX.Engine;

// Emitted through the outbox after a step of an edge-routed execution finishes. Running it
// POST-COMMIT (not inline in the finishing step) is what makes parallel correct: by the time it
// executes, sibling lanes' commits are visible, so a join's predecessors are all seen terminal and
// it is dispatched exactly once — and the last lane to commit observes every sibling as terminal,
// so there's no lost wakeup. Routing, atomic claims and a guarded completion UPDATE all live here.
public sealed record AdvanceExecution(Guid ExecutionId, int FromOrder);

public static class AdvanceExecutionHandler
{
    public static async Task<object?> HandleAsync(
        AdvanceExecution message,
        AutomateXDbContext dbContext,
        EngineEventBus eventBus,
        IOptions<EngineOptions> engineOptions,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var execution = await dbContext.Executions
            .Include(x => x.Steps)
            .FirstOrDefaultAsync(x => x.Id == message.ExecutionId, cancellationToken);

        if (execution is null || execution.Status is not ExecutionStatus.Running)
        {
            return null; // already terminal (failed by a step, or completed by another advance)
        }

        var edges = await dbContext.WorkflowEdges
            .AsNoTracking()
            .Where(x => x.WorkflowVersionId == execution.WorkflowVersionId)
            .Select(x => new WorkflowEdgeDef(x.FromOrder, x.ToOrder, x.Label))
            .ToListAsync(cancellationToken);

        var outgoing = new OutgoingMessages();

        var finished = execution.Steps.FirstOrDefault(x => x.StepOrder == message.FromOrder);
        if (finished is { Status: ExecutionStatus.Succeeded })
        {
            foreach (var order in await DispatchFromAsync(
                dbContext, execution, finished.StepOrder, finished.ActionType, finished.Output, edges, cancellationToken))
            {
                outgoing.Add(new ExecuteStep(execution.Id, order));
            }
        }

        // Complete exactly once — only while still Running and with no step Running (the freshly
        // claimed successors above are Running, so a dispatch keeps the execution open).
        var completed = await dbContext.Executions
            .Where(x => x.Id == message.ExecutionId
                && x.Status == ExecutionStatus.Running
                && !dbContext.StepExecutions.Any(s => s.ExecutionId == message.ExecutionId && s.Status == ExecutionStatus.Running))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.Status, ExecutionStatus.Succeeded)
                    .SetProperty(x => x.CompletedAt, DateTimeOffset.UtcNow),
                cancellationToken);

        if (completed > 0)
        {
            execution.Complete(); // reflect on the tracked instance for chain-trigger collection
            outgoing.AddRange(await WorkflowChaining.CollectAsync(dbContext, engineOptions.Value, execution, logger, cancellationToken));
            await eventBus.PublishAsync(new ExecutionCompleted(execution.Id, execution.WorkflowId), cancellationToken);
            logger.LogInformation("Execution {ExecutionId} completed (all lanes finished)", execution.Id);
        }

        return outgoing;
    }

    // Route the finished step, skip the not-taken lanes, and dispatch each successor that is ready
    // (all incoming predecessors terminal). Each dispatch/skip is an atomic claim, so a join reached
    // by two lanes at once starts exactly once.
    private static async Task<List<int>> DispatchFromAsync(
        AutomateXDbContext dbContext,
        Execution execution,
        int finishedOrder,
        string actionType,
        string? output,
        IReadOnlyList<WorkflowEdgeDef> edges,
        CancellationToken cancellationToken)
    {
        var chosenLabel = actionType == Switch.ActionType
            ? Switch.ChosenLabel(output) ?? Switch.DefaultLabel
            : null;
        var decision = WorkflowRouter.Route(finishedOrder, edges, chosenLabel);

        var actionByOrder = await dbContext.WorkflowSteps
            .AsNoTracking()
            .Where(x => x.WorkflowVersionId == execution.WorkflowVersionId)
            .Select(x => new { x.Order, x.ActionType })
            .ToDictionaryAsync(x => x.Order, x => x.ActionType, cancellationToken);

        foreach (var skipOrder in decision.Skipped)
        {
            if (actionByOrder.TryGetValue(skipOrder, out var skipAction))
            {
                await ClaimAsync(dbContext, execution.Id, skipOrder, skipAction, ExecutionStatus.Skipped, cancellationToken);
            }
        }

        var statuses = await dbContext.StepExecutions
            .AsNoTracking()
            .Where(x => x.ExecutionId == execution.Id)
            .Select(x => new { x.StepOrder, x.Status })
            .ToDictionaryAsync(x => x.StepOrder, x => x.Status, cancellationToken);

        bool Terminal(int order) =>
            statuses.TryGetValue(order, out var s) && s is ExecutionStatus.Succeeded or ExecutionStatus.Skipped or ExecutionStatus.Failed;
        bool Succeeded(int order) => statuses.TryGetValue(order, out var s) && s == ExecutionStatus.Succeeded;

        List<int> dispatched = [];
        foreach (var target in decision.Next)
        {
            if (WorkflowRouter.Readiness(target, edges, Terminal, Succeeded) == StepReadiness.Ready
                && actionByOrder.TryGetValue(target, out var action)
                && await ClaimAsync(dbContext, execution.Id, target, action, ExecutionStatus.Running, cancellationToken))
            {
                dispatched.Add(target);
            }
        }

        return dispatched;
    }

    // Insert the step row if absent (atomic claim). Returns true if this caller created it — i.e.
    // won the right to run/skip it. The unique index on (ExecutionId, StepOrder) backs ON CONFLICT.
    private static async Task<bool> ClaimAsync(
        AutomateXDbContext dbContext,
        Guid executionId,
        int stepOrder,
        string actionType,
        ExecutionStatus status,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        DateTimeOffset? completedAt = status == ExecutionStatus.Running ? null : now;
        var statusText = status.ToString();

        var rows = await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "StepExecutions" ("Id", "ExecutionId", "StepOrder", "ActionType", "Status", "Attempts", "StartedAt", "CompletedAt")
            VALUES ({Guid.CreateVersion7()}, {executionId}, {stepOrder}, {actionType}, {statusText}, 0, {now}, {completedAt})
            ON CONFLICT ("ExecutionId", "StepOrder") DO NOTHING
            """,
            cancellationToken);

        return rows > 0;
    }
}
