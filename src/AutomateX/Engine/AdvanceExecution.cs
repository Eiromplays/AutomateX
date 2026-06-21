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

        // Route from the finished step whether it succeeded, failed, or was caught: a Succeeded step
        // dispatches its ready successors; a Failed step (continue-on-failure) propagates skips to the
        // successors that depended on it; a Caught step routes its error lane.
        var finished = execution.Steps.FirstOrDefault(x => x.StepOrder == message.FromOrder);
        if (finished is { Status: ExecutionStatus.Succeeded or ExecutionStatus.Failed or ExecutionStatus.Caught })
        {
            foreach (var order in await DispatchFromAsync(
                dbContext, execution, finished.StepOrder, finished.ActionType, finished.Output,
                finished.Status == ExecutionStatus.Caught, edges, cancellationToken))
            {
                outgoing.Add(new ExecuteStep(execution.Id, order));
            }
        }

        // Settle exactly once, only with no step Running (the freshly claimed successors above are
        // Running, so a dispatch keeps the execution open). A failed step (continue-on-failure mode)
        // makes the final status Failed; otherwise Succeeded. The status read sits just before the
        // guarded UPDATE — benign, since a failing step emits its own AdvanceExecution.
        var anyRunning = await dbContext.StepExecutions
            .AnyAsync(s => s.ExecutionId == message.ExecutionId && s.Status == ExecutionStatus.Running, cancellationToken);
        if (anyRunning)
        {
            return outgoing;
        }

        var anyFailed = await dbContext.StepExecutions
            .AnyAsync(s => s.ExecutionId == message.ExecutionId && s.Status == ExecutionStatus.Failed, cancellationToken);
        var finalStatus = anyFailed ? ExecutionStatus.Failed : ExecutionStatus.Succeeded;

        var settled = await dbContext.Executions
            .Where(x => x.Id == message.ExecutionId
                && x.Status == ExecutionStatus.Running
                && !dbContext.StepExecutions.Any(s => s.ExecutionId == message.ExecutionId && s.Status == ExecutionStatus.Running))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.Status, finalStatus)
                    .SetProperty(x => x.CompletedAt, DateTimeOffset.UtcNow),
                cancellationToken);

        if (settled > 0)
        {
            // Reflect on the tracked instance so chain-trigger collection reads the final status.
            if (finalStatus == ExecutionStatus.Succeeded)
            {
                execution.Complete();
            }
            else
            {
                execution.Fail();
            }

            outgoing.AddRange(await WorkflowChaining.CollectAsync(dbContext, engineOptions.Value, execution, logger, cancellationToken));

            if (finalStatus == ExecutionStatus.Succeeded)
            {
                await eventBus.PublishAsync(new ExecutionCompleted(execution.Id, execution.WorkflowId), cancellationToken);
            }
            else
            {
                await eventBus.PublishAsync(new ExecutionFailed(execution.Id, execution.WorkflowId), cancellationToken);
            }

            logger.LogInformation("Execution {ExecutionId} settled {Status} (all lanes finished)", execution.Id, finalStatus);
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
        bool onError,
        IReadOnlyList<WorkflowEdgeDef> edges,
        CancellationToken cancellationToken)
    {
        var chosenLabel = !onError && actionType == Switch.ActionType
            ? Switch.ChosenLabel(output) ?? Switch.DefaultLabel
            : null;
        var decision = WorkflowRouter.Route(finishedOrder, edges, chosenLabel, onError);

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

        // The error lane is triggered by the failure itself: dispatch its target(s) directly rather
        // than through join-readiness, whose "failed/caught predecessor → skip" rule would block it.
        if (onError)
        {
            List<int> caught = [];
            foreach (var target in decision.Next)
            {
                if (actionByOrder.TryGetValue(target, out var action)
                    && await ClaimAsync(dbContext, execution.Id, target, action, ExecutionStatus.Running, cancellationToken))
                {
                    caught.Add(target);
                }
            }

            return caught;
        }

        // A local overlay over the committed statuses: claims made in this pass are reflected here
        // so a propagated skip is visible to the next target's readiness check.
        var local = await dbContext.StepExecutions
            .AsNoTracking()
            .Where(x => x.ExecutionId == execution.Id)
            .Select(x => new { x.StepOrder, x.Status })
            .ToDictionaryAsync(x => x.StepOrder, x => x.Status, cancellationToken);

        foreach (var skipOrder in decision.Skipped)
        {
            local[skipOrder] = ExecutionStatus.Skipped;
        }

        bool Terminal(int order) =>
            local.TryGetValue(order, out var s)
            && s is ExecutionStatus.Succeeded or ExecutionStatus.Skipped or ExecutionStatus.Failed or ExecutionStatus.Caught;
        bool Succeeded(int order) => local.TryGetValue(order, out var s) && s == ExecutionStatus.Succeeded;
        bool Failed(int order) => local.TryGetValue(order, out var s) && s == ExecutionStatus.Failed;

        List<int> dispatched = [];
        Queue<int> queue = new(decision.Next);
        HashSet<int> seen = [];

        while (queue.Count > 0)
        {
            var target = queue.Dequeue();
            if (!seen.Add(target) || !actionByOrder.TryGetValue(target, out var action))
            {
                continue;
            }

            switch (WorkflowRouter.Readiness(target, edges, Terminal, Succeeded, Failed))
            {
                case StepReadiness.Ready:
                    if (await ClaimAsync(dbContext, execution.Id, target, action, ExecutionStatus.Running, cancellationToken))
                    {
                        local[target] = ExecutionStatus.Running;
                        dispatched.Add(target);
                    }

                    break;

                case StepReadiness.Skip:
                    await ClaimAsync(dbContext, execution.Id, target, action, ExecutionStatus.Skipped, cancellationToken);
                    local[target] = ExecutionStatus.Skipped;
                    // A skipped node may free its own successors (a downstream join, or further skips).
                    foreach (var edge in edges)
                    {
                        if (edge.From == target)
                        {
                            queue.Enqueue(edge.To);
                        }
                    }

                    break;

                case StepReadiness.Wait:
                    break;
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
