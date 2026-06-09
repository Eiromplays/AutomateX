namespace AutomateX.Modules.State;

// One durable "remember between runs" entry, owned by a workflow. The key is
// free-form so callers express finer scopes by convention (e.g. trigger:<id>:seen:<x>);
// the value is plaintext JSON (NOT a secret store — secrets live in connections).
// An optional ExpiresAt gives cheap "max age" retention.
public sealed class WorkflowState
{
    private WorkflowState()
    {
    }

    public Guid WorkflowId { get; private set; }

    public string Key { get; private set; } = null!;

    public string Value { get; private set; } = null!;

    public DateTimeOffset? ExpiresAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static WorkflowState Create(Guid workflowId, string key, string value, DateTimeOffset? expiresAt) => new()
    {
        WorkflowId = workflowId,
        Key = key,
        Value = value,
        ExpiresAt = expiresAt,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    public void Set(string value, DateTimeOffset? expiresAt)
    {
        Value = value;
        ExpiresAt = expiresAt;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public bool IsExpired(DateTimeOffset now) => ExpiresAt is { } expiry && expiry <= now;
}
