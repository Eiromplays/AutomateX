namespace AutomateX.Modules.Audit;

// One append-only audit record: who did what to which target, when. WorkspaceId is null for
// instance-level events. Never updated or deleted by the app (retention prunes by At).
public sealed class AuditEntry
{
    private AuditEntry()
    {
    }

    public Guid Id { get; private set; }

    public DateTimeOffset At { get; private set; }

    // Subject/email of the caller, or "api-key" for a machine client / open instance.
    public string Actor { get; private set; } = null!;

    public Guid? WorkspaceId { get; private set; }

    // Dotted verb: "workflow.create", "connection.delete", "execution.failed", …
    public string Action { get; private set; } = null!;

    public string? TargetType { get; private set; }

    public string? TargetId { get; private set; }

    // Short human context (a name, a version bump) — never secrets.
    public string? Summary { get; private set; }

    public static AuditEntry Create(
        string actor, Guid? workspaceId, string action, string? targetType, string? targetId, string? summary) => new()
    {
        Id = Guid.CreateVersion7(),
        At = DateTimeOffset.UtcNow,
        Actor = actor,
        WorkspaceId = workspaceId,
        Action = action,
        TargetType = targetType,
        TargetId = targetId,
        Summary = summary,
    };
}
