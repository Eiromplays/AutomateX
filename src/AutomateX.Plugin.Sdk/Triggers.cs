using Microsoft.Extensions.Logging;

namespace AutomateX.Plugin.Sdk;

[AttributeUsage(AttributeTargets.Class)]
public sealed class TriggerAttribute(string type, string displayName) : Attribute
{
    public string Type { get; } = type;

    public string DisplayName { get; } = displayName;

    public string? Description { get; init; }
}

// A long-running listener: fire workflows via context.FireAsync whenever your
// event source produces something. Returning ends the cycle (the engine re-runs
// you after a short delay — the polling pattern); throwing restarts with backoff.
public interface ITriggerListener<TConfig>
{
    Task RunAsync(TConfig config, TriggerContext context, CancellationToken cancellationToken);
}

public sealed class TriggerContext
{
    public required ILogger Logger { get; init; }

    public required HttpClient Http { get; init; }

    public Guid TriggerId { get; init; }

    public Guid WorkflowId { get; init; }

    public required Func<string?, Task> Fire { get; init; }

    // payloadJson lands in the execution as {{trigger.payload}}.
    public Task FireAsync(string? payloadJson = null) => Fire(payloadJson);
}
