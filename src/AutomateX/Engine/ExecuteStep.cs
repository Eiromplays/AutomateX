using System.Text.Json;
using AutomateX.Database;
using AutomateX.Engine.Actions;
using AutomateX.Engine.Events;
using AutomateX.Engine.Templating;
using AutomateX.Modules.Executions;
using AutomateX.Plugin.Sdk;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Wolverine;

namespace AutomateX.Engine;

public sealed record ExecuteStep(Guid ExecutionId, int StepOrder);

public static class ExecuteStepHandler
{
    public static async Task<object?> HandleAsync(
        ExecuteStep message,
        AutomateXDbContext dbContext,
        ActionRegistry actions,
        EngineEventBus eventBus,
        IOptions<EngineOptions> engineOptions,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var options = engineOptions.Value;

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
            await eventBus.PublishAsync(new ExecutionFailed(execution.Id, execution.WorkflowId), cancellationToken);
            return null;
        }

        var stepExecution = execution.Steps.FirstOrDefault(x => x.StepOrder == message.StepOrder);
        if (stepExecution is { Status: ExecutionStatus.Succeeded })
        {
            // Redelivered after a crash between step completion and the cascade —
            // advance without re-executing or re-emitting the step's events.
            return await AdvanceAsync(execution, message.StepOrder, dbContext, eventBus, cancellationToken);
        }

        if (stepExecution is null)
        {
            stepExecution = execution.AddStep(step.ActionType, message.StepOrder);
            // Explicit Add: with client-set keys, entities discovered via a tracked parent's
            // navigation are assumed to exist (Modified), which saves as an UPDATE hitting 0 rows.
            dbContext.StepExecutions.Add(stepExecution);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        string resolvedConfig;
        try
        {
            resolvedConfig = TemplateResolver.Resolve(step.ConfigJson, BuildTemplateContext(execution));
        }
        catch (TemplateResolutionException ex)
        {
            // Deterministic config error — the action never runs and retries can't help.
            stepExecution.Fail(ex.Message);
            execution.Fail();
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogError(
                "Execution {ExecutionId} failed at step {StepOrder}: {Error}",
                execution.Id, message.StepOrder, ex.Message);
            await eventBus.PublishAsync(
                new StepFailed(execution.Id, message.StepOrder, step.ActionType, ex.Message, stepExecution.Attempts, WillRetry: false),
                cancellationToken);
            await eventBus.PublishAsync(new ExecutionFailed(execution.Id, execution.WorkflowId), cancellationToken);
            return null;
        }

        string? output;
        try
        {
            var invocation = new ActionInvocation(execution.Id, execution.WorkflowId, message.StepOrder);
            output = await actions.Get(step.ActionType).ExecuteAsync(resolvedConfig, invocation, cancellationToken);
        }
        catch (Exception ex)
        {
            stepExecution.RecordFailure(ex.Message);
            var willRetry = stepExecution.Attempts < options.MaxStepAttempts;

            if (!willRetry)
            {
                stepExecution.Fail(ex.Message);
                execution.Fail();
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await eventBus.PublishAsync(
                new StepFailed(execution.Id, message.StepOrder, step.ActionType, ex.Message, stepExecution.Attempts, willRetry),
                cancellationToken);

            if (!willRetry)
            {
                logger.LogError(ex,
                    "Execution {ExecutionId} failed at step {StepOrder} after {Attempts} attempts",
                    execution.Id, message.StepOrder, stepExecution.Attempts);
                await eventBus.PublishAsync(new ExecutionFailed(execution.Id, execution.WorkflowId), cancellationToken);
                return null;
            }

            var delays = options.StepRetryDelays;
            var delay = delays.Length > 0
                ? delays[Math.Min(stepExecution.Attempts - 1, delays.Length - 1)]
                : TimeSpan.FromSeconds(5);
            logger.LogWarning(
                "Step {StepOrder} of execution {ExecutionId} failed (attempt {Attempts}/{MaxAttempts}), retrying in {Delay}: {Error}",
                message.StepOrder, execution.Id, stepExecution.Attempts, options.MaxStepAttempts, delay, ex.Message);

            return new ExecuteStep(message.ExecutionId, message.StepOrder).DelayedFor(delay);
        }

        stepExecution.Complete(output);

        var nextOrder = await NextOrderAsync(dbContext, execution.WorkflowVersionId, message.StepOrder, cancellationToken);
        if (nextOrder is null)
        {
            execution.Complete();
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await eventBus.PublishAsync(
            new StepCompleted(execution.Id, message.StepOrder, step.ActionType, output), cancellationToken);

        if (nextOrder is null)
        {
            await eventBus.PublishAsync(new ExecutionCompleted(execution.Id, execution.WorkflowId), cancellationToken);
            return null;
        }

        return new ExecuteStep(execution.Id, nextOrder.Value);
    }

    private static async Task<object?> AdvanceAsync(
        Execution execution,
        int currentOrder,
        AutomateXDbContext dbContext,
        EngineEventBus eventBus,
        CancellationToken cancellationToken)
    {
        var nextOrder = await NextOrderAsync(dbContext, execution.WorkflowVersionId, currentOrder, cancellationToken);
        if (nextOrder is null)
        {
            execution.Complete();
            await dbContext.SaveChangesAsync(cancellationToken);
            await eventBus.PublishAsync(new ExecutionCompleted(execution.Id, execution.WorkflowId), cancellationToken);
            return null;
        }

        return new ExecuteStep(execution.Id, nextOrder.Value);
    }

    private static TemplateContext BuildTemplateContext(Execution execution)
    {
        Dictionary<int, JsonElement> stepOutputs = [];
        foreach (var step in execution.Steps.Where(x => x.Status == ExecutionStatus.Succeeded))
        {
            stepOutputs[step.StepOrder] = ParseOutput(step.Output);
        }

        return new TemplateContext(
            ParseOptionalJson(execution.TriggerPayload),
            stepOutputs,
            execution.Id,
            execution.WorkflowId);
    }

    private static JsonElement ParseOutput(string? output)
    {
        if (output is null)
        {
            return JsonSerializer.SerializeToElement<object?>(null);
        }

        try
        {
            return JsonSerializer.Deserialize<JsonElement>(output);
        }
        catch (JsonException)
        {
            // Non-JSON outputs stay addressable as plain strings via steps.N.output.
            return JsonSerializer.SerializeToElement(output);
        }
    }

    private static JsonElement? ParseOptionalJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<JsonElement>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static Task<int?> NextOrderAsync(
        AutomateXDbContext dbContext,
        Guid workflowVersionId,
        int currentOrder,
        CancellationToken cancellationToken) =>
        dbContext.WorkflowSteps
            .Where(x => x.WorkflowVersionId == workflowVersionId && x.Order > currentOrder)
            .OrderBy(x => x.Order)
            .Select(x => (int?)x.Order)
            .FirstOrDefaultAsync(cancellationToken);
}
