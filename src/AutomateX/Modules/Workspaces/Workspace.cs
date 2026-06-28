namespace AutomateX.Modules.Workspaces;

public sealed class Workspace
{
    // Well-known id: pre-workspace data adopts it via migration default, and requests
    // without an X-Workspace-Id header resolve to it.
    public static readonly Guid DefaultId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private Workspace()
    {
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }

    // The environment whose variable values runs use by default; null falls back to the 'default'
    // environment. A per-execution override can still pick another.
    public Guid? ActiveEnvironmentId { get; private set; }

    public void SetActiveEnvironment(Guid? environmentId) => ActiveEnvironmentId = environmentId;

    public static Workspace Create(string name) => new()
    {
        Id = Guid.CreateVersion7(),
        Name = name,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    public static Workspace CreateDefault() => new()
    {
        Id = DefaultId,
        Name = "Default",
        CreatedAt = DateTimeOffset.UtcNow,
    };

    public void Rename(string name)
    {
        Name = name;
    }
}
