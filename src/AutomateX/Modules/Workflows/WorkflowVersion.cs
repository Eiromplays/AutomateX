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

    // When true, a failed step in one parallel lane doesn't halt the run — other lanes finish and
    // the execution then settles Failed. When false (default), the first failure halts everything.
    public bool ContinueOnFailure { get; private set; }

    public List<WorkflowStep> Steps { get; } = [];

    public List<WorkflowEdge> Edges { get; } = [];

    internal static WorkflowVersion Create(
        Guid workflowId,
        int version,
        IReadOnlyList<StepDefinition> steps,
        IReadOnlyList<EdgeDefinition>? edges = null,
        bool continueOnFailure = false)
    {
        var workflowVersion = new WorkflowVersion
        {
            Id = Guid.CreateVersion7(),
            WorkflowId = workflowId,
            Version = version,
            CreatedAt = DateTimeOffset.UtcNow,
            ContinueOnFailure = continueOnFailure,
        };

        var takenKeys = new HashSet<string>(StringComparer.Ordinal);
        for (var order = 0; order < steps.Count; order++)
        {
            var definition = steps[order];
            var baseKey = StepKey.Slugify(
                string.IsNullOrWhiteSpace(definition.Key) ? definition.Name : definition.Key, order);
            var key = StepKey.Unique(baseKey, takenKeys);
            workflowVersion.Steps.Add(WorkflowStep.Create(workflowVersion.Id, order, definition, key));
        }

        foreach (var edge in edges ?? [])
        {
            workflowVersion.Edges.Add(WorkflowEdge.Create(workflowVersion.Id, edge));
        }

        return workflowVersion;
    }
}
