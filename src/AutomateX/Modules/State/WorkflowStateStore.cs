using AutomateX.Database;
using Microsoft.EntityFrameworkCore;

namespace AutomateX.Modules.State;

public sealed class WorkflowStateStore(AutomateXDbContext dbContext) : IWorkflowStateStore
{
    public async Task<string?> GetAsync(Guid workflowId, string key, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var entry = await dbContext.WorkflowStates
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.WorkflowId == workflowId && x.Key == key, cancellationToken);

        return entry is null || entry.IsExpired(now) ? null : entry.Value;
    }

    public async Task SetAsync(
        Guid workflowId, string key, string value, TimeSpan? ttl = null, CancellationToken cancellationToken = default)
    {
        var expiresAt = Expiry(ttl);
        var entry = await dbContext.WorkflowStates
            .FirstOrDefaultAsync(x => x.WorkflowId == workflowId && x.Key == key, cancellationToken);

        if (entry is null)
        {
            dbContext.WorkflowStates.Add(WorkflowState.Create(workflowId, key, value, expiresAt));
        }
        else
        {
            entry.Set(value, expiresAt);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> SetIfAbsentAsync(
        Guid workflowId, string key, string value, TimeSpan? ttl = null, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var expiresAt = Expiry(ttl, now);

        // Single atomic statement: insert when free; on key clash, take it over only
        // if the existing entry has expired (the WHERE). A live entry => 0 rows => false.
        var affected = await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "WorkflowStates" ("WorkflowId", "Key", "Value", "ExpiresAt", "UpdatedAt")
            VALUES ({workflowId}, {key}, {value}, {expiresAt}::timestamptz, {now})
            ON CONFLICT ("WorkflowId", "Key") DO UPDATE
                SET "Value" = EXCLUDED."Value",
                    "ExpiresAt" = EXCLUDED."ExpiresAt",
                    "UpdatedAt" = EXCLUDED."UpdatedAt"
                WHERE "WorkflowStates"."ExpiresAt" IS NOT NULL
                  AND "WorkflowStates"."ExpiresAt" <= {now}
            """,
            cancellationToken);

        return affected > 0;
    }

    public async Task<bool> RemoveAsync(Guid workflowId, string key, CancellationToken cancellationToken = default)
    {
        var affected = await dbContext.WorkflowStates
            .Where(x => x.WorkflowId == workflowId && x.Key == key)
            .ExecuteDeleteAsync(cancellationToken);

        return affected > 0;
    }

    public async Task<IReadOnlyList<WorkflowStateItem>> ListByPrefixAsync(
        Guid workflowId, string prefix, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        return await dbContext.WorkflowStates
            .AsNoTracking()
            .Where(x => x.WorkflowId == workflowId
                && x.Key.StartsWith(prefix)
                && (x.ExpiresAt == null || x.ExpiresAt > now))
            .OrderBy(x => x.Key)
            .Select(x => new WorkflowStateItem(x.Key, x.Value, x.ExpiresAt, x.UpdatedAt))
            .ToListAsync(cancellationToken);
    }

    private static DateTimeOffset? Expiry(TimeSpan? ttl, DateTimeOffset? now = null) =>
        ttl is { } span ? (now ?? DateTimeOffset.UtcNow).Add(span) : null;
}
