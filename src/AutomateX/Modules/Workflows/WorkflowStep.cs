namespace AutomateX.Modules.Workflows;

// Key is the stable reference identity ({{steps.<key>.output…}}); null lets the version
// auto-derive a slug from Name. Display Name stays freely editable without breaking refs.
// IdempotencyKey (optional, templated) makes a side-effecting step at-most-once: the engine caches
// the first successful result for the resolved key and returns it on any later run with the same key.
public sealed record StepDefinition(
    string ActionType, string? Name, string ConfigJson, string? Key = null, string? IdempotencyKey = null);

public sealed class WorkflowStep
{
    private WorkflowStep()
    {
    }

    public Guid Id { get; private set; }

    public Guid WorkflowVersionId { get; private set; }

    public int Order { get; private set; }

    public string? Name { get; private set; }

    // Stable per-version slug used by templating; references bind to this, not Order or Name.
    public string Key { get; private set; } = null!;

    public string ActionType { get; private set; } = null!;

    public string ConfigJson { get; private set; } = null!;

    // Optional templated idempotency key (e.g. {{trigger.payload.orderId}}); null = run every time.
    public string? IdempotencyKey { get; private set; }

    internal static WorkflowStep Create(Guid workflowVersionId, int order, StepDefinition definition, string key) => new()
    {
        Id = Guid.CreateVersion7(),
        WorkflowVersionId = workflowVersionId,
        Order = order,
        Name = definition.Name,
        Key = key,
        ActionType = definition.ActionType,
        ConfigJson = definition.ConfigJson,
        IdempotencyKey = string.IsNullOrWhiteSpace(definition.IdempotencyKey) ? null : definition.IdempotencyKey,
    };
}
