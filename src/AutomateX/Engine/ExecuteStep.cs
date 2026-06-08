using System.Text.Json;
using AutomateX.Database;
using AutomateX.Engine.Actions;
using AutomateX.Engine.Events;
using AutomateX.Engine.Security;
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
        SecretCipher cipher,
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
            var chains = await WorkflowChaining.CollectAsync(dbContext, options, execution, logger, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogError(
                "Step {StepOrder} not found for execution {ExecutionId}, marking failed",
                message.StepOrder, message.ExecutionId);
            await eventBus.PublishAsync(new ExecutionFailed(execution.Id, execution.WorkflowId), cancellationToken);
            return Cascade(chains);
        }

        var stepExecution = execution.Steps.FirstOrDefault(x => x.StepOrder == message.StepOrder);
        if (stepExecution is { Status: ExecutionStatus.Succeeded })
        {
            // Redelivered after a crash between step completion and the cascade —
            // advance without re-executing or re-emitting the step's events.
            return await AdvanceAsync(execution, message.StepOrder, dbContext, eventBus, options, logger, cancellationToken);
        }

        if (stepExecution is null)
        {
            stepExecution = execution.AddStep(step.ActionType, message.StepOrder);
            // Explicit Add: with client-set keys, entities discovered via a tracked parent's
            // navigation are assumed to exist (Modified), which saves as an UPDATE hitting 0 rows.
            dbContext.StepExecutions.Add(stepExecution);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var secretSink = new HashSet<string>();
        string resolvedConfig;
        try
        {
            var templateContext = await BuildTemplateContextAsync(execution, step.ConfigJson, dbContext, cipher, cancellationToken)
                with { SecretSink = secretSink };
            resolvedConfig = TemplateResolver.Resolve(step.ConfigJson, templateContext);
        }
        catch (Exception ex) when (ex is TemplateResolutionException or SecretCipherException)
        {
            // Deterministic config error — the action never runs and retries can't help.
            stepExecution.Fail(ex.Message);
            execution.Fail();
            var chains = await WorkflowChaining.CollectAsync(dbContext, options, execution, logger, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogError(
                "Execution {ExecutionId} failed at step {StepOrder}: {Error}",
                execution.Id, message.StepOrder, ex.Message);
            await eventBus.PublishAsync(
                new StepFailed(execution.Id, message.StepOrder, step.ActionType, ex.Message, stepExecution.Attempts, WillRetry: false),
                cancellationToken);
            await eventBus.PublishAsync(new ExecutionFailed(execution.Id, execution.WorkflowId), cancellationToken);
            return Cascade(chains);
        }

        string? output;
        try
        {
            var invocation = new ActionInvocation(execution.Id, execution.WorkflowId, message.StepOrder);
            output = await actions.Get(step.ActionType, execution.WorkspaceId).ExecuteAsync(resolvedConfig, invocation, cancellationToken);
        }
        catch (Exception ex)
        {
            // Connection secrets are masked in everything persisted or published.
            var error = SecretMasker.MaskSecrets(ex.Message, secretSink) ?? ex.Message;

            stepExecution.RecordFailure(error);
            var willRetry = stepExecution.Attempts < options.MaxStepAttempts;

            List<RunWorkflow> failureChains = [];
            if (!willRetry)
            {
                stepExecution.Fail(error);
                execution.Fail();
                failureChains = await WorkflowChaining.CollectAsync(dbContext, options, execution, logger, cancellationToken);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await eventBus.PublishAsync(
                new StepFailed(execution.Id, message.StepOrder, step.ActionType, error, stepExecution.Attempts, willRetry),
                cancellationToken);

            if (!willRetry)
            {
                logger.LogError(ex,
                    "Execution {ExecutionId} failed at step {StepOrder} after {Attempts} attempts",
                    execution.Id, message.StepOrder, stepExecution.Attempts);
                await eventBus.PublishAsync(new ExecutionFailed(execution.Id, execution.WorkflowId), cancellationToken);
                return Cascade(failureChains);
            }

            var delays = options.StepRetryDelays;
            var delay = delays.Length > 0
                ? delays[Math.Min(stepExecution.Attempts - 1, delays.Length - 1)]
                : TimeSpan.FromSeconds(5);
            logger.LogWarning(
                "Step {StepOrder} of execution {ExecutionId} failed (attempt {Attempts}/{MaxAttempts}), retrying in {Delay}: {Error}",
                message.StepOrder, execution.Id, stepExecution.Attempts, options.MaxStepAttempts, delay, error);

            return new ExecuteStep(message.ExecutionId, message.StepOrder).DelayedFor(delay);
        }

        // Connection secrets are masked in everything persisted or published.
        output = SecretMasker.MaskSecrets(output, secretSink);
        stepExecution.Complete(output);

        // A closed gate halts the workflow cleanly: later steps are recorded as
        // Skipped and the execution still Succeeds — stopping the chain is normal flow.
        if (step.ActionType == Gate.ActionType && Gate.IsClosed(output))
        {
            var remaining = await dbContext.WorkflowSteps
                .AsNoTracking()
                .Where(x => x.WorkflowVersionId == execution.WorkflowVersionId && x.Order > message.StepOrder)
                .OrderBy(x => x.Order)
                .Select(x => new { x.Order, x.ActionType })
                .ToListAsync(cancellationToken);

            foreach (var skipped in remaining)
            {
                dbContext.StepExecutions.Add(execution.AddSkippedStep(skipped.ActionType, skipped.Order));
            }

            execution.Complete();
            var gateChains = await WorkflowChaining.CollectAsync(dbContext, options, execution, logger, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            await eventBus.PublishAsync(
                new StepCompleted(execution.Id, message.StepOrder, step.ActionType, output), cancellationToken);
            await eventBus.PublishAsync(new ExecutionCompleted(execution.Id, execution.WorkflowId), cancellationToken);
            logger.LogInformation(
                "Execution {ExecutionId} halted by a closed gate at step {StepOrder}; {Count} step(s) skipped",
                execution.Id, message.StepOrder, remaining.Count);
            return Cascade(gateChains);
        }

        var nextOrder = await NextOrderAsync(dbContext, execution.WorkflowVersionId, message.StepOrder, cancellationToken);
        List<RunWorkflow> successChains = [];
        if (nextOrder is null)
        {
            execution.Complete();
            successChains = await WorkflowChaining.CollectAsync(dbContext, options, execution, logger, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await eventBus.PublishAsync(
            new StepCompleted(execution.Id, message.StepOrder, step.ActionType, output), cancellationToken);

        if (nextOrder is null)
        {
            await eventBus.PublishAsync(new ExecutionCompleted(execution.Id, execution.WorkflowId), cancellationToken);
            return Cascade(successChains);
        }

        return new ExecuteStep(execution.Id, nextOrder.Value);
    }

    private static async Task<object?> AdvanceAsync(
        Execution execution,
        int currentOrder,
        AutomateXDbContext dbContext,
        EngineEventBus eventBus,
        EngineOptions options,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var nextOrder = await NextOrderAsync(dbContext, execution.WorkflowVersionId, currentOrder, cancellationToken);
        if (nextOrder is null)
        {
            execution.Complete();
            var chains = await WorkflowChaining.CollectAsync(dbContext, options, execution, logger, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            await eventBus.PublishAsync(new ExecutionCompleted(execution.Id, execution.WorkflowId), cancellationToken);
            return Cascade(chains);
        }

        return new ExecuteStep(execution.Id, nextOrder.Value);
    }

    // Chained RunWorkflow messages cascade through the same outbox as step messages.
    private static object? Cascade(List<RunWorkflow> chains)
    {
        if (chains.Count == 0)
        {
            return null;
        }

        var outgoing = new OutgoingMessages();
        outgoing.AddRange(chains);
        return outgoing;
    }

    private static async Task<TemplateContext> BuildTemplateContextAsync(
        Execution execution,
        string configJson,
        AutomateXDbContext dbContext,
        SecretCipher cipher,
        CancellationToken cancellationToken)
    {
        Dictionary<int, JsonElement> stepOutputs = [];
        foreach (var step in execution.Steps.Where(x => x.Status == ExecutionStatus.Succeeded))
        {
            stepOutputs[step.StepOrder] = ParseOutput(step.Output);
        }

        // Decrypt connections only when the config can possibly reference them —
        // and only the workflow's own workspace's connections (isolation boundary).
        Dictionary<string, JsonElement>? connections = null;
        if (configJson.Contains("connections", StringComparison.Ordinal))
        {
            connections = [];
            var workspaceConnections = await dbContext.Connections
                .AsNoTracking()
                .Where(x => x.WorkspaceId == execution.WorkspaceId)
                .ToListAsync(cancellationToken);
            foreach (var connection in workspaceConnections)
            {
                connections[connection.Name] = JsonSerializer.Deserialize<JsonElement>(cipher.Decrypt(connection.EncryptedSecrets));
            }
        }

        return new TemplateContext(
            ParseOptionalJson(execution.TriggerPayload),
            stepOutputs,
            execution.Id,
            execution.WorkflowId,
            connections);
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
