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

    public List<WorkflowEdge> Edges { get; } = [];

    internal static WorkflowVersion Create(
        Guid workflowId, int version, IReadOnlyList<StepDefinition> steps, IReadOnlyList<EdgeDefinition>? edges = null)
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

        foreach (var edge in edges ?? [])
        {
            workflowVersion.Edges.Add(WorkflowEdge.Create(workflowVersion.Id, edge));
        }

        return workflowVersion;
    }
}
