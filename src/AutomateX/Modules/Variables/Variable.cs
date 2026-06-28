namespace AutomateX.Modules.Variables;

// A named, reusable value referenced as {{vars.<name>}}. WorkflowId null = workspace scope (shared by
// every workflow); set = a workflow-scoped override that shadows a workspace variable of the same name.
// Secret variables have their per-environment values encrypted at rest (workspace DEK) and masked.
public sealed class Variable
{
    private Variable()
    {
    }

    public Guid Id { get; private set; }

    public Guid WorkspaceId { get; private set; }

    public Guid? WorkflowId { get; private set; }

    public string Name { get; private set; } = null!;

    public bool Secret { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public List<VariableValue> Values { get; } = [];

    public static Variable Create(Guid workspaceId, Guid? workflowId, string name, bool secret) => new()
    {
        Id = Guid.CreateVersion7(),
        WorkspaceId = workspaceId,
        WorkflowId = workflowId,
        Name = name,
        Secret = secret,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    public void Rename(string name) => Name = name;
}
