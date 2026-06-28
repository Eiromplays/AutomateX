using AutomateX.Database;

namespace AutomateX.Modules.Audit;

// Records audit entries. Call AFTER the mutation has been saved — the sink appends its own row, so
// it never flushes a half-built handler change set.
public interface IAuditSink
{
    Task RecordAsync(
        string action,
        Guid? workspaceId,
        string actor,
        string? targetType = null,
        string? targetId = null,
        string? summary = null,
        CancellationToken cancellationToken = default);
}

public sealed class AuditSink(AutomateXDbContext dbContext) : IAuditSink
{
    public async Task RecordAsync(
        string action,
        Guid? workspaceId,
        string actor,
        string? targetType = null,
        string? targetId = null,
        string? summary = null,
        CancellationToken cancellationToken = default)
    {
        dbContext.AuditEntries.Add(AuditEntry.Create(actor, workspaceId, action, targetType, targetId, summary));
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
