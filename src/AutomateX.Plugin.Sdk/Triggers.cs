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

// Durable "remember between runs" state, scoped to this trigger (the host isolates
// keys per trigger). The dedup tool: SetIfAbsentAsync returns true once for a fresh
// key, false while a live entry holds it — so an item fires at most once.
public interface ITriggerState
{
    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);

    Task SetAsync(string key, string value, TimeSpan? ttl = null, CancellationToken cancellationToken = default);

    Task<bool> SetIfAbsentAsync(string key, string value, TimeSpan? ttl = null, CancellationToken cancellationToken = default);

    Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default);
}

// Default when no engine state is wired (e.g. unit tests constructing a context):
// stateless, and every key reads as new so a trigger degrades to fire-always.
public sealed class NullTriggerState : ITriggerState
{
    public static readonly NullTriggerState Instance = new();

    private NullTriggerState()
    {
    }

    public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);

    public Task SetAsync(string key, string value, TimeSpan? ttl = null, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<bool> SetIfAbsentAsync(string key, string value, TimeSpan? ttl = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);

    public Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);
}

public sealed class TriggerContext
{
    public required ILogger Logger { get; init; }

    public required HttpClient Http { get; init; }

    public Guid TriggerId { get; init; }

    public Guid WorkflowId { get; init; }

    public required Func<string?, Task> Fire { get; init; }

    public ITriggerState State { get; init; } = NullTriggerState.Instance;

    // payloadJson lands in the execution as {{trigger.payload}}.
    public Task FireAsync(string? payloadJson = null) => Fire(payloadJson);

    // Dedup helper: true the first time this id is seen (now recorded), false after.
    // Pair with FireAsync — `if (await ctx.MarkNewAsync(id)) await ctx.FireAsync(...)`.
    public Task<bool> MarkNewAsync(string key, TimeSpan? ttl = null) => State.SetIfAbsentAsync(key, "1", ttl);
}
