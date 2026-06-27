using Microsoft.Extensions.Logging;

namespace AutomateX.Plugin.Sdk;

// Host-provided services plus execution metadata for an action invocation.
public sealed class ActionContext
{
    public required ILogger Logger { get; init; }

    public required HttpClient Http { get; init; }

    public Guid ExecutionId { get; init; }

    public Guid WorkflowId { get; init; }

    public int StepOrder { get; init; }

    // The step's resolved idempotency key, if set — actions may forward it to providers that support
    // request-level dedup (e.g. an Idempotency-Key header), complementing the engine's result cache.
    public string? IdempotencyKey { get; init; }
}
