using AutomateX.Modules.State;
using AutomateX.Plugin.Sdk;
using Microsoft.Extensions.DependencyInjection;

namespace AutomateX.Engine.Actions;

// Built-in actions over the durable per-workflow KV store (IWorkflowStateStore). Keys are
// namespaced under "kv:" so workflow-authored values can't collide with a trigger's internal
// dedup state, and everything is scoped to the running workflow (context.WorkflowId).
//
// The store is scoped (DbContext-backed) while these action instances are singletons, so each
// call opens its own scope — same pattern as WorkflowScopedTriggerState.

public sealed record KvGetConfig(string Key);

public sealed record KvGetResult(bool Found, string? Value);

public sealed record KvSetConfig(string Key, [property: Multiline] string Value, int? TtlSeconds = null);

public sealed record KvSetResult(bool Ok);

public sealed record KvSetIfAbsentConfig(string Key, [property: Multiline] string Value = "1", int? TtlSeconds = null);

public sealed record KvSetIfAbsentResult(bool Acquired);

public sealed record KvDeleteConfig(string Key);

public sealed record KvDeleteResult(bool Removed);

internal static class Kv
{
    public static string Scoped(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("kv requires a non-empty 'key'.");
        }

        return $"kv:{key}";
    }

    public static TimeSpan? Ttl(int? seconds) => seconds is > 0 ? TimeSpan.FromSeconds(seconds.Value) : null;
}

[Action("kv.get", "KV: Get",
    Description = "Reads a value from the workflow's durable key/value store (scoped to this workflow). "
        + "Output: found (bool) and value (the string, or null). Pair with a gate or template into later steps.")]
public sealed class KvGetAction(IServiceScopeFactory scopeFactory) : IAction<KvGetConfig, KvGetResult>
{
    public async Task<KvGetResult> ExecuteAsync(KvGetConfig config, ActionContext context, CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IWorkflowStateStore>();
        var value = await store.GetAsync(context.WorkflowId, Kv.Scoped(config.Key), cancellationToken);
        return new KvGetResult(value is not null, value);
    }
}

[Action("kv.set", "KV: Set",
    Description = "Writes a value to the workflow's durable key/value store. Optional ttlSeconds expires it "
        + "after that long. Overwrites any existing value. Output: ok.")]
public sealed class KvSetAction(IServiceScopeFactory scopeFactory) : IAction<KvSetConfig, KvSetResult>
{
    public async Task<KvSetResult> ExecuteAsync(KvSetConfig config, ActionContext context, CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IWorkflowStateStore>();
        await store.SetAsync(context.WorkflowId, Kv.Scoped(config.Key), config.Value, Kv.Ttl(config.TtlSeconds), cancellationToken);
        return new KvSetResult(true);
    }
}

[Action("kv.setIfAbsent", "KV: Set if absent (dedup)",
    Description = "Atomically claims a key: sets it only if it isn't already held (fresh, or its TTL expired). "
        + "Output: acquired — true the first time, false if already held. The dedup primitive: gate on acquired "
        + "to make a workflow run once per key, e.g. key 'deployed:{{trigger.payload.json.0.tag_name}}'.")]
public sealed class KvSetIfAbsentAction(IServiceScopeFactory scopeFactory) : IAction<KvSetIfAbsentConfig, KvSetIfAbsentResult>
{
    public async Task<KvSetIfAbsentResult> ExecuteAsync(KvSetIfAbsentConfig config, ActionContext context, CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IWorkflowStateStore>();
        var acquired = await store.SetIfAbsentAsync(
            context.WorkflowId, Kv.Scoped(config.Key), config.Value, Kv.Ttl(config.TtlSeconds), cancellationToken);
        return new KvSetIfAbsentResult(acquired);
    }
}

[Action("kv.delete", "KV: Delete",
    Description = "Removes a key from the workflow's durable key/value store. Output: removed (true if a live "
        + "entry was deleted).")]
public sealed class KvDeleteAction(IServiceScopeFactory scopeFactory) : IAction<KvDeleteConfig, KvDeleteResult>
{
    public async Task<KvDeleteResult> ExecuteAsync(KvDeleteConfig config, ActionContext context, CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IWorkflowStateStore>();
        var removed = await store.RemoveAsync(context.WorkflowId, Kv.Scoped(config.Key), cancellationToken);
        return new KvDeleteResult(removed);
    }
}
