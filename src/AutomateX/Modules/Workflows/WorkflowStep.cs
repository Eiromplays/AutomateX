namespace AutomateX.Modules.Workflows;

public sealed record StepDefinition(string ActionType, string? Name, string ConfigJson);

public sealed class WorkflowStep
{
    private WorkflowStep()
    {
    }

    public Guid Id { get; private set; }

    public Guid WorkflowVersionId { get; private set; }

    public int Order { get; private set; }

    public string? Name { get; private set; }

    public string ActionType { get; private set; } = null!;

    public string ConfigJson { get; private set; } = null!;

    internal static WorkflowStep Create(Guid workflowVersionId, int order, StepDefinition definition) => new()
    {
        Id = Guid.CreateVersion7(),
        WorkflowVersionId = workflowVersionId,
        Order = order,
        Name = definition.Name,
        ActionType = definition.ActionType,
        ConfigJson = definition.ConfigJson,
    };
}
