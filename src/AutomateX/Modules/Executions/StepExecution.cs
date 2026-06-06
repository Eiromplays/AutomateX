namespace AutomateX.Modules.Executions;

public sealed class StepExecution
{
    private StepExecution()
    {
    }

    public Guid Id { get; private set; }

    public Guid ExecutionId { get; private set; }

    public int StepOrder { get; private set; }

    public string ActionType { get; private set; } = null!;

    public ExecutionStatus Status { get; private set; }

    // Completed invocations of the action, success included. Failed attempts are
    // derived: Attempts - 1 when Succeeded, Attempts otherwise.
    public int Attempts { get; private set; }

    public string? Output { get; private set; }

    public string? Error { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    internal static StepExecution Start(Guid executionId, string actionType, int stepOrder) => new()
    {
        Id = Guid.CreateVersion7(),
        ExecutionId = executionId,
        StepOrder = stepOrder,
        ActionType = actionType,
        Status = ExecutionStatus.Running,
        StartedAt = DateTimeOffset.UtcNow,
    };

    public void RecordFailure(string error)
    {
        Attempts++;
        Error = error;
    }

    public void Complete(string? output)
    {
        Attempts++;
        Status = ExecutionStatus.Succeeded;
        Output = output;
        Error = null;
        CompletedAt = DateTimeOffset.UtcNow;
    }

    public void Fail(string error)
    {
        Status = ExecutionStatus.Failed;
        Error = error;
        CompletedAt = DateTimeOffset.UtcNow;
    }
}
