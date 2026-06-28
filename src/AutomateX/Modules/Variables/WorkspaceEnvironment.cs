namespace AutomateX.Modules.Variables;

// A named value set for a workspace (e.g. default / staging / prod). Each variable holds one value per
// environment; the active environment (or a per-run override) selects which a run sees. Named
// WorkspaceEnvironment to avoid clashing with System.Environment; the API surfaces it as "environment".
public sealed class WorkspaceEnvironment
{
    public const string DefaultName = "default";

    private WorkspaceEnvironment()
    {
    }

    public Guid Id { get; private set; }

    public Guid WorkspaceId { get; private set; }

    public string Name { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }

    public static WorkspaceEnvironment Create(Guid workspaceId, string name) => new()
    {
        Id = Guid.CreateVersion7(),
        WorkspaceId = workspaceId,
        Name = name,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    public void Rename(string name) => Name = name;
}
