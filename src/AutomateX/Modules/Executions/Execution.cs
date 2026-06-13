using AutomateX.Modules.Workspaces;

namespace AutomateX.Modules.Executions;

public sealed class Execution
{
    private Execution()
    {
    }

    public Guid Id { get; private set; }

    // Denormalized from the workflow at run start — cheap history filtering + scoping.
    public Guid WorkspaceId { get; private set; }

    public Guid WorkflowId { get; private set; }

    public Guid WorkflowVersionId { get; private set; }

    public string TriggeredBy { get; private set; } = null!;

    // Raw JSON delivered by the trigger (webhook/manual body); template root {{trigger.payload}}.
    public string? TriggerPayload { get; private set; }

    public ExecutionStatus Status { get; private set; }

    // Denormalized from the version: whether a lane failure halts the run or lets others finish.
    public bool ContinueOnFailure { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public List<StepExecution> Steps { get; } = [];

    public static Execution Start(
        Guid id,
        Guid workflowId,
        Guid workflowVersionId,
        string triggeredBy,
        string? triggerPayload = null,
        Guid? workspaceId = null,
        bool continueOnFailure = false) => new()
    {
        Id = id,
        WorkspaceId = workspaceId ?? Workspace.DefaultId,
        WorkflowId = workflowId,
        WorkflowVersionId = workflowVersionId,
        TriggeredBy = triggeredBy,
        TriggerPayload = triggerPayload,
        Status = ExecutionStatus.Running,
        ContinueOnFailure = continueOnFailure,
        StartedAt = DateTimeOffset.UtcNow,
    };

    public StepExecution AddStep(string actionType, int stepOrder)
    {
        var step = StepExecution.Start(Id, actionType, stepOrder);
        Steps.Add(step);
        return step;
    }

    public StepExecution AddSkippedStep(string actionType, int stepOrder)
    {
        var step = StepExecution.Skipped(Id, actionType, stepOrder);
        Steps.Add(step);
        return step;
    }

    public void Complete()
    {
        Status = ExecutionStatus.Succeeded;
        CompletedAt = DateTimeOffset.UtcNow;
    }

    public void Fail()
    {
        Status = ExecutionStatus.Failed;
        CompletedAt = DateTimeOffset.UtcNow;
    }
}
