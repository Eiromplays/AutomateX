namespace AutomateX.Modules.Workflows;

public sealed class WorkflowVersion
{
    private WorkflowVersion()
    {
    }

    public Guid Id { get; private set; }

    public Guid WorkflowId { get; private set; }

    public int Version { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public List<WorkflowStep> Steps { get; } = [];

    internal static WorkflowVersion Create(Guid workflowId, int version, IReadOnlyList<StepDefinition> steps)
    {
        var workflowVersion = new WorkflowVersion
        {
            Id = Guid.CreateVersion7(),
            WorkflowId = workflowId,
            Version = version,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        for (var order = 0; order < steps.Count; order++)
        {
            workflowVersion.Steps.Add(WorkflowStep.Create(workflowVersion.Id, order, steps[order]));
        }

        return workflowVersion;
    }
}
