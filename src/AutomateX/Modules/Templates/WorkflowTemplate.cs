namespace AutomateX.Modules.Templates;

// A saved, reusable workflow template — a portable WorkflowTransfer.Export document (secrets excluded;
// connections + variables as name references) plus gallery metadata. Workspace-shared: any member sees
// the workspace's saved templates. "Use" flows through the same import-review screen as a file import.
public sealed class WorkflowTemplate
{
    private WorkflowTemplate()
    {
    }

    public Guid Id { get; private set; }

    public Guid WorkspaceId { get; private set; }

    public string Name { get; private set; } = null!;

    public string? Description { get; private set; }

    public string? Category { get; private set; }

    // The WorkflowTransfer.Export JSON, stored verbatim.
    public string Doc { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }

    public static WorkflowTemplate Create(
        Guid workspaceId, string name, string? description, string? category, string doc) => new()
    {
        Id = Guid.CreateVersion7(),
        WorkspaceId = workspaceId,
        Name = name,
        Description = description,
        Category = category,
        Doc = doc,
        CreatedAt = DateTimeOffset.UtcNow,
    };
}
