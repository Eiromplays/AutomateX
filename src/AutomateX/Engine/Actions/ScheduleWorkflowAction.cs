using AutomateX.Database;
using AutomateX.Plugin.Sdk;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace AutomateX.Engine.Actions;

public static class ScheduleResolution
{
    public static DateTimeOffset Resolve(int? delaySeconds, DateTimeOffset? runAt, DateTimeOffset now)
    {
        if (delaySeconds is null == (runAt is null))
        {
            throw new ArgumentException("schedule.workflow needs exactly one of 'delaySeconds' or 'runAt'.");
        }

        var when = runAt ?? now.AddSeconds(delaySeconds!.Value);
        if (when <= now)
        {
            throw new ArgumentException("The scheduled time must be in the future.");
        }

        return when;
    }
}

public sealed record ScheduleWorkflowConfig(
    Guid WorkflowId,
    int? DelaySeconds = null,
    DateTimeOffset? RunAt = null,
    string? Payload = null);

public sealed record ScheduleWorkflowResult(Guid ScheduledExecutionId, DateTimeOffset RunAt);

// A privileged BUILT-IN (not an SDK plugin — it needs engine internals): exposes
// Wolverine's durable scheduling so a workflow can queue a future run of another
// workflow. One-shot — the scheduled message fires once and is gone; "remind me
// in 2 hours" needs no cleanup. Cross-workspace scheduling is refused.
[Action("schedule.workflow", "Schedule: Run a Workflow Later",
    Description = "Schedules another workflow to run later (delaySeconds OR runAt), passing an optional "
        + "payload it receives as {{trigger.payload}}. Durable — survives restarts. The target must live "
        + "in the same workspace. Pair with llm.prompt + matrix to build natural-language reminders.")]
public sealed class ScheduleWorkflowAction(IServiceScopeFactory scopeFactory)
    : IAction<ScheduleWorkflowConfig, ScheduleWorkflowResult>
{
    public async Task<ScheduleWorkflowResult> ExecuteAsync(
        ScheduleWorkflowConfig config,
        ActionContext context,
        CancellationToken cancellationToken = default)
    {
        if (config.WorkflowId == Guid.Empty)
        {
            throw new ArgumentException("schedule.workflow requires 'workflowId'.");
        }

        var runAt = ScheduleResolution.Resolve(config.DelaySeconds, config.RunAt, DateTimeOffset.UtcNow);

        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AutomateXDbContext>();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        var callerWorkspace = await dbContext.Executions
            .Where(x => x.Id == context.ExecutionId)
            .Select(x => (Guid?)x.WorkspaceId)
            .FirstOrDefaultAsync(cancellationToken);

        var targetWorkspace = await dbContext.Workflows
            .Where(x => x.Id == config.WorkflowId)
            .Select(x => (Guid?)x.WorkspaceId)
            .FirstOrDefaultAsync(cancellationToken);

        if (targetWorkspace is null)
        {
            throw new InvalidOperationException($"Target workflow {config.WorkflowId} does not exist.");
        }

        if (callerWorkspace is not null && targetWorkspace != callerWorkspace)
        {
            throw new InvalidOperationException("schedule.workflow cannot target a workflow in another workspace.");
        }

        var scheduledExecutionId = Guid.CreateVersion7();
        await bus.ScheduleAsync(
            new RunWorkflow(scheduledExecutionId, config.WorkflowId, "scheduled", config.Payload), runAt);

        context.Logger.LogInformation(
            "Scheduled workflow {WorkflowId} to run at {RunAt} (execution {ExecutionId})",
            config.WorkflowId, runAt, scheduledExecutionId);

        return new ScheduleWorkflowResult(scheduledExecutionId, runAt);
    }
}
