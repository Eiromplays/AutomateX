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

    // Set when this run is a sub-workflow call: who to resume when it reaches a terminal state.
    public Guid? ParentExecutionId { get; private set; }

    public int? ParentStepOrder { get; private set; }

    // For a forEach child: which item slot its result fills in the parent's accumulator.
    public int? ParentItemIndex { get; private set; }

    // Sub-workflow nesting depth (0 = top level); guarded against runaway recursion.
    public int Depth { get; private set; }

    // The environment this run resolved {{vars.x}} against — stamped at start so history shows it and a
    // replay reuses it. Null when the workspace has no environments configured.
    public Guid? EnvironmentId { get; private set; }

    public List<StepExecution> Steps { get; } = [];

    public static Execution Start(
        Guid id,
        Guid workflowId,
        Guid workflowVersionId,
        string triggeredBy,
        string? triggerPayload = null,
        Guid? workspaceId = null,
        bool continueOnFailure = false,
        Guid? parentExecutionId = null,
        int? parentStepOrder = null,
        int depth = 0,
        int? parentItemIndex = null,
        Guid? environmentId = null) => new()
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
        ParentExecutionId = parentExecutionId,
        ParentStepOrder = parentStepOrder,
        ParentItemIndex = parentItemIndex,
        Depth = depth,
        EnvironmentId = environmentId,
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

    // Pause at a wait step; resumed by a timer or external signal (see ResumeExecution).
    public void Suspend() => Status = ExecutionStatus.Waiting;

    public void Resume() => Status = ExecutionStatus.Running;

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
