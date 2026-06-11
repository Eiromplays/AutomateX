namespace AutomateX.Modules.Workflows;

public sealed record EdgeDefinition(int FromOrder, int ToOrder, string? Label);

// A directed link between two steps of a version (by Order). A null Label is an
// unconditional ("always") link; labelled edges are the outcomes of a switch step.
// A version with no edges runs linearly by Order — branching is opt-in.
public sealed class WorkflowEdge
{
    private WorkflowEdge()
    {
    }

    public Guid Id { get; private set; }

    public Guid WorkflowVersionId { get; private set; }

    public int FromOrder { get; private set; }

    public int ToOrder { get; private set; }

    public string? Label { get; private set; }

    internal static WorkflowEdge Create(Guid workflowVersionId, EdgeDefinition definition) => new()
    {
        Id = Guid.CreateVersion7(),
        WorkflowVersionId = workflowVersionId,
        FromOrder = definition.FromOrder,
        ToOrder = definition.ToOrder,
        Label = definition.Label,
    };
}
