using System.Text.Json;
using AutomateX.Database;
using AutomateX.Engine.Actions;
using AutomateX.Engine.Connections;
using AutomateX.Engine.Events;
using AutomateX.Engine.Security;
using AutomateX.Engine.Templating;
using AutomateX.Modules.Executions;
using AutomateX.Modules.Idempotency;
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
        ConnectionResolver connectionResolver,
        Modules.Variables.VariableLoader variableLoader,
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
            return await AdvanceAsync(
                execution, message.StepOrder, step.ActionType, stepExecution.Output,
                dbContext, eventBus, options, logger, cancellationToken);
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
        string? idempotencyKey = null;
        try
        {
            var templateContext = await BuildTemplateContextAsync(execution, step.ConfigJson, dbContext, connectionResolver, variableLoader, cancellationToken)
                with { SecretSink = secretSink };
            resolvedConfig = TemplateResolver.Resolve(step.ConfigJson, templateContext);

            if (!string.IsNullOrWhiteSpace(step.IdempotencyKey))
            {
                var resolvedKey = TemplateResolver.ResolveString(step.IdempotencyKey, templateContext);
                idempotencyKey = string.IsNullOrWhiteSpace(resolvedKey) ? null : resolvedKey;
            }
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

        // wait is engine-handled: suspend the run instead of invoking an action.
        if (step.ActionType == Wait.ActionType)
        {
            return await SuspendForWaitAsync(
                execution, stepExecution, message.StepOrder, resolvedConfig, dbContext, eventBus, options, logger, cancellationToken);
        }

        // workflow.call is engine-handled: start a child run and suspend until it finishes.
        if (step.ActionType == WorkflowCall.ActionType)
        {
            return await StartSubWorkflowAsync(
                execution, stepExecution, message.StepOrder, resolvedConfig, dbContext, eventBus, options, logger, cancellationToken);
        }

        // forEach is engine-handled: map a child run over the items, suspend, accumulate the results.
        if (step.ActionType == ForEach.ActionType)
        {
            return await StartForEachAsync(
                execution, stepExecution, message.StepOrder, resolvedConfig, dbContext, eventBus, options, logger, cancellationToken);
        }

        // Idempotency: a keyed step returns its first success's cached result instead of re-invoking —
        // dedups re-fires of the same logical event and post-commit redeliveries.
        if (idempotencyKey is not null)
        {
            var cached = await dbContext.IdempotencyRecords
                .AsNoTracking()
                .Where(x => x.WorkflowId == execution.WorkflowId && x.Key == idempotencyKey)
                .Select(x => new { x.Result })
                .FirstOrDefaultAsync(cancellationToken);

            if (cached is not null)
            {
                stepExecution.Complete(cached.Result);
                await dbContext.SaveChangesAsync(cancellationToken);
                await eventBus.PublishAsync(
                    new StepCompleted(execution.Id, message.StepOrder, step.ActionType, cached.Result), cancellationToken);
                logger.LogInformation(
                    "Execution {ExecutionId} step {StepOrder} returned a cached result for its idempotency key (no re-invoke)",
                    execution.Id, message.StepOrder);
                return await AdvanceAsync(
                    execution, message.StepOrder, step.ActionType, cached.Result,
                    dbContext, eventBus, options, logger, cancellationToken);
            }
        }

        string? output;
        try
        {
            var invocation = new ActionInvocation(execution.Id, execution.WorkflowId, message.StepOrder, idempotencyKey);
            output = await actions.Get(step.ActionType, execution.WorkspaceId).ExecuteAsync(resolvedConfig, invocation, cancellationToken);
        }
        catch (Exception ex)
        {
            // Connection secrets are masked in everything persisted or published.
            var error = SecretMasker.MaskSecrets(ex.Message, secretSink) ?? ex.Message;

            stepExecution.RecordFailure(error);

            if (stepExecution.Attempts < options.MaxStepAttempts)
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                await eventBus.PublishAsync(
                    new StepFailed(execution.Id, message.StepOrder, step.ActionType, error, stepExecution.Attempts, WillRetry: true),
                    cancellationToken);

                var delays = options.StepRetryDelays;
                var delay = delays.Length > 0
                    ? delays[Math.Min(stepExecution.Attempts - 1, delays.Length - 1)]
                    : TimeSpan.FromSeconds(5);
                logger.LogWarning(
                    "Step {StepOrder} of execution {ExecutionId} failed (attempt {Attempts}/{MaxAttempts}), retrying in {Delay}: {Error}",
                    message.StepOrder, execution.Id, stepExecution.Attempts, options.MaxStepAttempts, delay, error);

                return new ExecuteStep(message.ExecutionId, message.StepOrder).DelayedFor(delay);
            }

            // Out of retries — this step has failed for good. An error edge handles it: record
            // Caught (not Failed), route the error lane, don't fail the execution. Error handling
            // wins over both halt and continue-on-failure.
            var hasErrorEdge = await dbContext.WorkflowEdges.AnyAsync(
                x => x.WorkflowVersionId == execution.WorkflowVersionId
                    && x.FromOrder == message.StepOrder
                    && x.Label == Edges.ErrorLabel,
                cancellationToken);

            if (hasErrorEdge)
            {
                stepExecution.Catch(error);
                await dbContext.SaveChangesAsync(cancellationToken);
                await eventBus.PublishAsync(
                    new StepFailed(execution.Id, message.StepOrder, step.ActionType, error, stepExecution.Attempts, WillRetry: false),
                    cancellationToken);
                logger.LogWarning(
                    "Execution {ExecutionId} step {StepOrder} failed and was caught by an error edge",
                    execution.Id, message.StepOrder);
                return new AdvanceExecution(execution.Id, message.StepOrder);
            }

            stepExecution.Fail(error);

            // Continue-on-failure: only this lane dies; other lanes finish and the execution
            // settles Failed once nothing is running (handled by AdvanceExecution).
            if (execution.ContinueOnFailure)
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                await eventBus.PublishAsync(
                    new StepFailed(execution.Id, message.StepOrder, step.ActionType, error, stepExecution.Attempts, WillRetry: false),
                    cancellationToken);
                logger.LogWarning(
                    "Execution {ExecutionId} step {StepOrder} failed; other lanes continue", execution.Id, message.StepOrder);
                return new AdvanceExecution(execution.Id, message.StepOrder);
            }

            // Halt: the first failure fails the whole execution; other lanes are abandoned.
            execution.Fail();
            var failureChains = await WorkflowChaining.CollectAsync(dbContext, options, execution, logger, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            await eventBus.PublishAsync(
                new StepFailed(execution.Id, message.StepOrder, step.ActionType, error, stepExecution.Attempts, WillRetry: false),
                cancellationToken);
            logger.LogError(ex,
                "Execution {ExecutionId} failed at step {StepOrder} after {Attempts} attempts",
                execution.Id, message.StepOrder, stepExecution.Attempts);
            await eventBus.PublishAsync(new ExecutionFailed(execution.Id, execution.WorkflowId), cancellationToken);
            return Cascade(failureChains);
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

        // Cache the result under the idempotency key in the same commit as the step's success, so a
        // re-fire or post-commit redelivery returns it instead of repeating the side effect.
        if (idempotencyKey is not null)
        {
            dbContext.IdempotencyRecords.Add(
                IdempotencyRecord.Create(execution.WorkspaceId, execution.WorkflowId, idempotencyKey, output));
        }

        // Persist this step's success before routing so readiness/claim queries see it.
        await dbContext.SaveChangesAsync(cancellationToken);
        await eventBus.PublishAsync(
            new StepCompleted(execution.Id, message.StepOrder, step.ActionType, output), cancellationToken);

        return await AdvanceAsync(
            execution, message.StepOrder, step.ActionType, output, dbContext, eventBus, options, logger, cancellationToken);
    }

    // Suspend the run at a wait step: record the step + execution Waiting and schedule a timer wake
    // (delay, or signal timeout). An indefinite signal wait returns nothing and waits for an external
    // ResumeExecution. A bad wait config fails deterministically, like a template error.
    private static async Task<object?> SuspendForWaitAsync(
        Execution execution,
        StepExecution stepExecution,
        int stepOrder,
        string resolvedConfig,
        AutomateXDbContext dbContext,
        EngineEventBus eventBus,
        EngineOptions options,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        WaitConfig config;
        var now = DateTimeOffset.UtcNow;
        DateTimeOffset? wakeAt;
        try
        {
            config = JsonSerializer.Deserialize<WaitConfig>(resolvedConfig, JsonSerializerOptions.Web) ?? new WaitConfig();
            wakeAt = Wait.WakeAt(config, now);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            stepExecution.Fail(ex.Message);
            execution.Fail();
            var chains = await WorkflowChaining.CollectAsync(dbContext, options, execution, logger, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            await eventBus.PublishAsync(
                new StepFailed(execution.Id, stepOrder, Wait.ActionType, ex.Message, stepExecution.Attempts, WillRetry: false),
                cancellationToken);
            await eventBus.PublishAsync(new ExecutionFailed(execution.Id, execution.WorkflowId), cancellationToken);
            return Cascade(chains);
        }

        stepExecution.Suspend();
        execution.Suspend();
        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Execution {ExecutionId} waiting at step {StepOrder} (wake {Wake})",
            execution.Id, stepOrder, wakeAt?.ToString("o") ?? "on signal");

        if (wakeAt is { } when)
        {
            var reason = Wait.IsSignal(config) ? "timeout" : "timer";
            var delay = when - now;
            return new ResumeExecution(execution.Id, stepOrder, reason, null)
                .DelayedFor(delay < TimeSpan.Zero ? TimeSpan.Zero : delay);
        }

        return null; // indefinite signal wait — resumed by an external ResumeExecution
    }

    // workflow.call: start a child run (carrying a parent link) and suspend until it finishes. The
    // child's terminal site cascades a ResumeExecution back here (see WorkflowChaining.CollectAsync).
    private static async Task<object?> StartSubWorkflowAsync(
        Execution execution,
        StepExecution stepExecution,
        int stepOrder,
        string resolvedConfig,
        AutomateXDbContext dbContext,
        EngineEventBus eventBus,
        EngineOptions options,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        WorkflowCallConfig config;
        try
        {
            config = JsonSerializer.Deserialize<WorkflowCallConfig>(resolvedConfig, JsonSerializerOptions.Web)
                ?? throw new ArgumentException("workflow.call requires a 'workflowId'.");
            if (config.WorkflowId == Guid.Empty)
            {
                throw new ArgumentException("workflow.call requires a 'workflowId'.");
            }
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            return await FailStepAsync(execution, stepExecution, stepOrder, WorkflowCall.ActionType, ex.Message,
                dbContext, eventBus, options, logger, cancellationToken);
        }

        var childDepth = execution.Depth + 1;
        var targetWorkspace = await dbContext.Workflows
            .Where(x => x.Id == config.WorkflowId)
            .Select(x => (Guid?)x.WorkspaceId)
            .FirstOrDefaultAsync(cancellationToken);

        if (targetWorkspace != execution.WorkspaceId)
        {
            return await FailStepAsync(execution, stepExecution, stepOrder, WorkflowCall.ActionType,
                "workflow.call target workflow was not found in this workspace.",
                dbContext, eventBus, options, logger, cancellationToken);
        }

        if (childDepth > options.MaxChainDepth)
        {
            return await FailStepAsync(execution, stepExecution, stepOrder, WorkflowCall.ActionType,
                $"workflow.call exceeded the maximum nesting depth ({options.MaxChainDepth}).",
                dbContext, eventBus, options, logger, cancellationToken);
        }

        stepExecution.Suspend();
        execution.Suspend();
        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Execution {ExecutionId} called workflow {WorkflowId} at step {StepOrder} (child depth {Depth})",
            execution.Id, config.WorkflowId, stepOrder, childDepth);

        return new RunWorkflow(
            Guid.CreateVersion7(), config.WorkflowId, $"call:{execution.Id}", config.Payload,
            EntryOrder: null, ParentExecutionId: execution.Id, ParentStepOrder: stepOrder, Depth: childDepth);
    }

    // forEach: map a child workflow over the items array. Empty → complete with []. Otherwise create
    // the accumulator, suspend, and launch the first item (sequential v1); the rest follow as each
    // child finishes (see ResumeExecutionHandler's forEach accumulation).
    private static async Task<object?> StartForEachAsync(
        Execution execution,
        StepExecution stepExecution,
        int stepOrder,
        string resolvedConfig,
        AutomateXDbContext dbContext,
        EngineEventBus eventBus,
        EngineOptions options,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        ForEachConfig config;
        try
        {
            config = JsonSerializer.Deserialize<ForEachConfig>(resolvedConfig, JsonSerializerOptions.Web)
                ?? throw new ArgumentException("forEach requires 'items' and 'workflowId'.");
            if (config.WorkflowId == Guid.Empty)
            {
                throw new ArgumentException("forEach requires a 'workflowId'.");
            }

            if (config.Items.ValueKind != JsonValueKind.Array)
            {
                throw new ArgumentException("forEach 'items' must be an array.");
            }
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            return await FailStepAsync(execution, stepExecution, stepOrder, ForEach.ActionType, ex.Message,
                dbContext, eventBus, options, logger, cancellationToken);
        }

        var total = config.Items.GetArrayLength();
        if (total == 0)
        {
            stepExecution.Complete("[]");
            await dbContext.SaveChangesAsync(cancellationToken);
            await eventBus.PublishAsync(new StepCompleted(execution.Id, stepOrder, ForEach.ActionType, "[]"), cancellationToken);
            return await AdvanceAsync(execution, stepOrder, ForEach.ActionType, "[]", dbContext, eventBus, options, logger, cancellationToken);
        }

        var childDepth = execution.Depth + 1;
        var targetWorkspace = await dbContext.Workflows
            .Where(x => x.Id == config.WorkflowId)
            .Select(x => (Guid?)x.WorkspaceId)
            .FirstOrDefaultAsync(cancellationToken);

        if (targetWorkspace != execution.WorkspaceId)
        {
            return await FailStepAsync(execution, stepExecution, stepOrder, ForEach.ActionType,
                "forEach target workflow was not found in this workspace.", dbContext, eventBus, options, logger, cancellationToken);
        }

        if (childDepth > options.MaxChainDepth)
        {
            return await FailStepAsync(execution, stepExecution, stepOrder, ForEach.ActionType,
                $"forEach exceeded the maximum nesting depth ({options.MaxChainDepth}).", dbContext, eventBus, options, logger, cancellationToken);
        }

        var state = ForEachState.Create(execution.Id, stepOrder, config.WorkflowId, config.Items.GetRawText(), total);
        dbContext.ForEachStates.Add(state);

        stepExecution.Suspend();
        execution.Suspend();

        var firstPayload = state.ItemPayload(0);
        state.TakeNext();
        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Execution {ExecutionId} forEach over {Total} item(s) at step {StepOrder}", execution.Id, total, stepOrder);

        return new RunWorkflow(
            Guid.CreateVersion7(), config.WorkflowId, $"foreach:{execution.Id}", firstPayload,
            EntryOrder: null, ParentExecutionId: execution.Id, ParentStepOrder: stepOrder, Depth: childDepth, ParentItemIndex: 0);
    }

    // Deterministic step failure: fail the step + execution, fan out terminal messages, emit events.
    private static async Task<object?> FailStepAsync(
        Execution execution,
        StepExecution stepExecution,
        int stepOrder,
        string actionType,
        string error,
        AutomateXDbContext dbContext,
        EngineEventBus eventBus,
        EngineOptions options,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        stepExecution.Fail(error);
        execution.Fail();
        var chains = await WorkflowChaining.CollectAsync(dbContext, options, execution, logger, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await eventBus.PublishAsync(
            new StepFailed(execution.Id, stepOrder, actionType, error, stepExecution.Attempts, WillRetry: false),
            cancellationToken);
        await eventBus.PublishAsync(new ExecutionFailed(execution.Id, execution.WorkflowId), cancellationToken);
        return Cascade(chains);
    }

    // Advance past a finished step. No edges → run linearly by Order with inline completion
    // (unchanged, single-in-flight). With edges → hand off to AdvanceExecution, which does the
    // routing, ready-successor dispatch and completion post-commit (so parallel joins are correct).
    internal static async Task<object?> AdvanceAsync(
        Execution execution,
        int currentOrder,
        string actionType,
        string? output,
        AutomateXDbContext dbContext,
        EngineEventBus eventBus,
        EngineOptions options,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var edges = await dbContext.WorkflowEdges
            .AsNoTracking()
            .Where(x => x.WorkflowVersionId == execution.WorkflowVersionId)
            .Select(x => new WorkflowEdgeDef(x.FromOrder, x.ToOrder, x.Label))
            .ToListAsync(cancellationToken);

        if (edges.Count == 0)
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

        // Edge-routed: routing, dispatch and completion happen post-commit in AdvanceExecution,
        // where sibling lanes' commits are visible (so joins dispatch once, no lost wakeup).
        return new AdvanceExecution(execution.Id, currentOrder);
    }

    // Terminal fan-out (chained RunWorkflows + a sub-workflow parent resume) cascades through the
    // same outbox as step messages.
    private static object? Cascade(List<object> messages)
    {
        if (messages.Count == 0)
        {
            return null;
        }

        var outgoing = new OutgoingMessages();
        outgoing.AddRange(messages);
        return outgoing;
    }

    private static async Task<TemplateContext> BuildTemplateContextAsync(
        Execution execution,
        string configJson,
        AutomateXDbContext dbContext,
        ConnectionResolver connectionResolver,
        Modules.Variables.VariableLoader variableLoader,
        CancellationToken cancellationToken)
    {
        Dictionary<int, JsonElement> stepOutputs = [];
        foreach (var step in execution.Steps.Where(x => x.Status == ExecutionStatus.Succeeded))
        {
            stepOutputs[step.StepOrder] = ParseOutput(step.Output);
        }

        // A failed/caught step's error is addressable on the error lane as {{steps.<key>.error}}.
        Dictionary<int, JsonElement> stepErrors = [];
        foreach (var step in execution.Steps.Where(
            x => x.Error is not null && x.Status is ExecutionStatus.Failed or ExecutionStatus.Caught))
        {
            stepErrors[step.StepOrder] = JsonSerializer.SerializeToElement(new { message = step.Error });
        }

        // Decrypt connections only when the config can possibly reference them — and only the
        // workflow's own workspace's connections (isolation boundary). OAuth tokens that are
        // expired get refreshed here, before the step runs.
        Dictionary<string, JsonElement>? connections = null;
        if (configJson.Contains("{{connections.", StringComparison.Ordinal))
        {
            var workspaceConnections = await dbContext.Connections
                .AsNoTracking()
                .Where(x => x.WorkspaceId == execution.WorkspaceId)
                .ToListAsync(cancellationToken);
            connections = await connectionResolver.ResolveAsync(workspaceConnections, cancellationToken);
        }

        // Only needed when a config references a step by key; numeric refs resolve from StepOutputs.
        Dictionary<string, int>? stepKeys = null;
        if (configJson.Contains("{{steps.", StringComparison.Ordinal))
        {
            stepKeys = await dbContext.WorkflowSteps
                .AsNoTracking()
                .Where(x => x.WorkflowVersionId == execution.WorkflowVersionId)
                .Select(x => new { x.Key, x.Order })
                .ToDictionaryAsync(x => x.Key, x => x.Order, cancellationToken);
        }

        IReadOnlyDictionary<string, string>? variables = null;
        IReadOnlySet<string>? secretVariableNames = null;
        if (configJson.Contains("{{vars.", StringComparison.Ordinal))
        {
            (variables, secretVariableNames) = await variableLoader.LoadAsync(
                execution.WorkspaceId, execution.WorkflowId, execution.EnvironmentId, cancellationToken);
        }

        return new TemplateContext(
            ParseOptionalJson(execution.TriggerPayload),
            stepOutputs,
            execution.Id,
            execution.WorkflowId,
            connections,
            stepKeys,
            stepErrors,
            Variables: variables,
            SecretVariableNames: secretVariableNames);
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
