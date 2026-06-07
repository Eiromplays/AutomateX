namespace AutomateX.Modules.Workspaces;

public enum WorkspaceRole
{
    Viewer = 0,
    Editor = 1,
    Owner = 2,
}

// Members are invited by email (they may not have signed in yet); the OIDC subject
// binds permanently on the first matching sign-in.
public sealed class WorkspaceMember
{
    private WorkspaceMember()
    {
    }

    public Guid Id { get; private set; }

    public Guid WorkspaceId { get; private set; }

    public string Email { get; private set; } = null!;

    public string? Subject { get; private set; }

    public WorkspaceRole Role { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static WorkspaceMember Create(Guid workspaceId, string email, WorkspaceRole role) => new()
    {
        Id = Guid.CreateVersion7(),
        WorkspaceId = workspaceId,
        Email = email.Trim().ToLowerInvariant(),
        Role = role,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    public void BindSubject(string subject)
    {
        Subject = subject;
    }

    public void ChangeRole(WorkspaceRole role)
    {
        Role = role;
    }
}

public static class LastOwnerGuard
{
    // A workspace must never lose its last Owner.
    public static bool CanRemoveOrDemote(IReadOnlyCollection<WorkspaceMember> members, WorkspaceMember target) =>
        target.Role != WorkspaceRole.Owner
        || members.Count(x => x.Role == WorkspaceRole.Owner) > 1;
}
