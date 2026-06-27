namespace AutomateX.Modules.Idempotency;

// The cached result of a side-effecting step's first success, keyed per workflow. A later run with
// the same resolved idempotency key returns Result without re-invoking the action. CreatedAt supports
// retention pruning. WorkspaceId is denormalized for scoping/cleanup; the PK is (WorkflowId, Key).
public sealed class IdempotencyRecord
{
    private IdempotencyRecord()
    {
    }

    public Guid WorkspaceId { get; private set; }

    public Guid WorkflowId { get; private set; }

    public string Key { get; private set; } = null!;

    // The first successful step output (JSON, may be null for an action that returns nothing).
    public string? Result { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static IdempotencyRecord Create(Guid workspaceId, Guid workflowId, string key, string? result) => new()
    {
        WorkspaceId = workspaceId,
        WorkflowId = workflowId,
        Key = key,
        Result = result,
        CreatedAt = DateTimeOffset.UtcNow,
    };
}
