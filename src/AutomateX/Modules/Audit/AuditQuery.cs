namespace AutomateX.Modules.Audit;

// Scoping + filtering for the audit read, factored out so the rules are unit-testable. A null
// scopeWorkspaceId means "all workspaces" (instance-admin); otherwise entries are limited to one.
public static class AuditQuery
{
    public static IQueryable<AuditEntry> Apply(
        IQueryable<AuditEntry> source,
        Guid? scopeWorkspaceId,
        string? actor = null,
        string? action = null,
        string? targetType = null,
        DateTimeOffset? since = null)
    {
        if (scopeWorkspaceId is { } ws)
        {
            source = source.Where(x => x.WorkspaceId == ws);
        }

        if (!string.IsNullOrWhiteSpace(actor))
        {
            source = source.Where(x => x.Actor == actor);
        }

        if (!string.IsNullOrWhiteSpace(action))
        {
            source = source.Where(x => x.Action == action);
        }

        if (!string.IsNullOrWhiteSpace(targetType))
        {
            source = source.Where(x => x.TargetType == targetType);
        }

        if (since is { } from)
        {
            source = source.Where(x => x.At >= from);
        }

        return source.OrderByDescending(x => x.At);
    }
}
