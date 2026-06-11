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

    public WorkflowVersion AddVersion(IReadOnlyList<StepDefinition> steps, IReadOnlyList<EdgeDefinition>? edges = null)
    {
        var nextVersion = Versions.Count == 0 ? 1 : Versions.Max(x => x.Version) + 1;
        var version = WorkflowVersion.Create(Id, nextVersion, steps, edges);
        Versions.Add(version);
        return version;
    }

    // Rollback = git revert, not git reset: a copy of the target's steps becomes the
    // newest version, so history stays append-only and executions stay pinned.
    public WorkflowVersion RestoreVersion(int version)
    {
        var target = Versions.FirstOrDefault(x => x.Version == version)
            ?? throw new InvalidOperationException($"Version {version} does not exist.");

        if (version == Versions.Max(x => x.Version))
        {
            throw new InvalidOperationException($"v{version} is already the latest version.");
        }

        return AddVersion(
            target.Steps
                .OrderBy(x => x.Order)
                .Select(x => new StepDefinition(x.ActionType, x.Name, x.ConfigJson))
                .ToList(),
            target.Edges
                .Select(x => new EdgeDefinition(x.FromOrder, x.ToOrder, x.Label))
                .ToList());
    }

    public void Rename(string name, string? description)
    {
        Name = name;
        Description = description;
    }
}
