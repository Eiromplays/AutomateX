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

    // A disabled workflow is paused at the source: the engine refuses to start runs for it, so
    // triggers, chains, scheduled and manual runs are all blocked until re-enabled.
    public bool Enabled { get; private set; } = true;

    public List<WorkflowVersion> Versions { get; } = [];

    public static Workflow Create(string name, string? description, Guid? workspaceId = null) => new()
    {
        Id = Guid.CreateVersion7(),
        WorkspaceId = workspaceId ?? Workspace.DefaultId,
        Name = name,
        Description = description,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    public void SetEnabled(bool enabled) => Enabled = enabled;

    public WorkflowVersion AddVersion(
        IReadOnlyList<StepDefinition> steps,
        IReadOnlyList<EdgeDefinition>? edges = null,
        bool continueOnFailure = false)
    {
        var nextVersion = Versions.Count == 0 ? 1 : Versions.Max(x => x.Version) + 1;
        var version = WorkflowVersion.Create(Id, nextVersion, steps, edges, continueOnFailure);
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
                .Select(x => new StepDefinition(x.ActionType, x.Name, x.ConfigJson, x.Key))
                .ToList(),
            target.Edges
                .Select(x => new EdgeDefinition(x.FromOrder, x.ToOrder, x.Label))
                .ToList(),
            target.ContinueOnFailure);
    }

    // Removes a past version from history. The latest version can't be removed (it's the live
    // definition); the caller is responsible for ensuring no execution references it.
    public WorkflowVersion RemoveVersion(int version)
    {
        var target = Versions.FirstOrDefault(x => x.Version == version)
            ?? throw new InvalidOperationException($"Version {version} does not exist.");

        if (version == Versions.Max(x => x.Version))
        {
            throw new InvalidOperationException($"v{version} is the latest version and can't be deleted.");
        }

        Versions.Remove(target);
        return target;
    }

    public void Rename(string name, string? description)
    {
        Name = name;
        Description = description;
    }
}
