using AutomateX.Modules.Workspaces;

namespace AutomateX.Modules.Workflows;

public sealed class Workflow
{
    private Workflow()
    {
    }

    public Guid Id { get; private set; }

    public Guid WorkspaceId { get; private set; }

    public string Name { get; private set; } = null!;

    public string? Description { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public List<WorkflowVersion> Versions { get; } = [];

    public static Workflow Create(string name, string? description, Guid? workspaceId = null) => new()
    {
        Id = Guid.CreateVersion7(),
        WorkspaceId = workspaceId ?? Workspace.DefaultId,
        Name = name,
        Description = description,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    public WorkflowVersion AddVersion(IReadOnlyList<StepDefinition> steps)
    {
        var nextVersion = Versions.Count == 0 ? 1 : Versions.Max(x => x.Version) + 1;
        var version = WorkflowVersion.Create(Id, nextVersion, steps);
        Versions.Add(version);
        return version;
    }

    public void Rename(string name, string? description)
    {
        Name = name;
        Description = description;
    }
}
