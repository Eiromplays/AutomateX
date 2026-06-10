using AutomateX.Modules.State;
using AutomateX.Plugin.Sdk;

namespace AutomateX.Engine.Triggers;

// Backs the SDK's ITriggerState with the durable WorkflowState store. The store is
// scoped, the context factory a singleton, so each call opens its own scope (same
// pattern as the host's per-fire scope). Keys are auto-namespaced per trigger, so two
// triggers on one workflow can't collide on a bare "seen:<id>" key.
public sealed class WorkflowScopedTriggerState(
    IServiceScopeFactory scopeFactory, Guid workflowId, Guid triggerId) : ITriggerState
{
    private string Scoped(string key) => $"trigger:{triggerId}:{key}";

    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        return await Store(scope).GetAsync(workflowId, Scoped(key), cancellationToken);
    }

    public async Task SetAsync(string key, string value, TimeSpan? ttl = null, CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        await Store(scope).SetAsync(workflowId, Scoped(key), value, ttl, cancellationToken);
    }

    public async Task<bool> SetIfAbsentAsync(string key, string value, TimeSpan? ttl = null, CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        return await Store(scope).SetIfAbsentAsync(workflowId, Scoped(key), value, ttl, cancellationToken);
    }

    public async Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        return await Store(scope).RemoveAsync(workflowId, Scoped(key), cancellationToken);
    }

    private static IWorkflowStateStore Store(AsyncServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<IWorkflowStateStore>();
}
