namespace AutomateX.Modules.Executions;

public sealed class Execution
{
    private Execution()
    {
    }

    public Guid Id { get; private set; }

    public Guid WorkflowId { get; private set; }

    public Guid WorkflowVersionId { get; private set; }

    public string TriggeredBy { get; private set; } = null!;

    public ExecutionStatus Status { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public List<StepExecution> Steps { get; } = [];

    public static Execution Start(Guid id, Guid workflowId, Guid workflowVersionId, string triggeredBy) => new()
    {
        Id = id,
        WorkflowId = workflowId,
        WorkflowVersionId = workflowVersionId,
        TriggeredBy = triggeredBy,
        Status = ExecutionStatus.Running,
        StartedAt = DateTimeOffset.UtcNow,
    };

    public StepExecution AddStep(string actionType, int stepOrder)
    {
        var step = StepExecution.Start(Id, actionType, stepOrder);
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
