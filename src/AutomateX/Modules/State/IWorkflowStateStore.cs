namespace AutomateX.Modules.State;

public sealed record WorkflowStateItem(string Key, string Value, DateTimeOffset? ExpiresAt, DateTimeOffset UpdatedAt);

// Durable per-workflow key/value state. Expired entries read as absent everywhere.
public interface IWorkflowStateStore
{
    Task<string?> GetAsync(Guid workflowId, string key, CancellationToken cancellationToken = default);

    Task SetAsync(Guid workflowId, string key, string value, TimeSpan? ttl = null, CancellationToken cancellationToken = default);

    // Atomic insert-if-absent — the dedup primitive. Returns true when the key was
    // free (fresh, or its previous entry had expired) and is now ours; false when a
    // live entry already holds it (left untouched).
    Task<bool> SetIfAbsentAsync(Guid workflowId, string key, string value, TimeSpan? ttl = null, CancellationToken cancellationToken = default);

    Task<bool> RemoveAsync(Guid workflowId, string key, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkflowStateItem>> ListByPrefixAsync(Guid workflowId, string prefix, CancellationToken cancellationToken = default);
}
