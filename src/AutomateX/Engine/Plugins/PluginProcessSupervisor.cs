using AutomateX.Database;
using AutomateX.Modules.State;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json.Nodes;
using Wolverine;

namespace AutomateX.Engine.Plugins;

// Owns one warm PluginHost process per plugin assembly and routes the engine's calls to it. As the
// IPluginHostCallbacks for every client, it backs trigger.fire with a RunWorkflow enqueue and
// trigger.state.* with the durable WorkflowState store — the same semantics as the in-proc host.
public sealed class PluginProcessSupervisor(
    IServiceScopeFactory scopeFactory, ILoggerFactory loggerFactory, string pluginHostDll)
    : IPluginHostCallbacks, IAsyncDisposable
{
    private sealed record TriggerInfo(Guid TriggerId, Guid WorkflowId, string Type, int? EntryStepOrder);

    private readonly ILogger _logger = loggerFactory.CreateLogger<PluginProcessSupervisor>();
    private readonly Dictionary<string, PluginClient> _clients = [];
    private readonly Dictionary<string, TriggerInfo> _triggers = new();
    private readonly object _clientsLock = new();
    private readonly object _triggersLock = new();

    public Task<JsonObject> DescribeAsync(string pluginDll, CancellationToken cancellationToken = default) =>
        Client(pluginDll).DescribeAsync(cancellationToken);

    public Task<string?> ExecuteActionAsync(string pluginDll, string type, string configJson, CancellationToken cancellationToken = default) =>
        Client(pluginDll).ExecuteActionAsync(type, configJson, cancellationToken);

    public Task<JsonObject> BuildOAuthConfigAsync(string pluginDll, string type, JsonObject values, CancellationToken cancellationToken = default) =>
        Client(pluginDll).BuildOAuthConfigAsync(type, values, cancellationToken);

    public Task<JsonObject> TestConnectionAsync(string pluginDll, string type, JsonObject values, CancellationToken cancellationToken = default) =>
        Client(pluginDll).TestConnectionAsync(type, values, cancellationToken);

    public void RunTrigger(string pluginDll, Guid triggerId, string type, Guid workflowId, int? entryStepOrder, string configJson)
    {
        lock (_triggersLock)
        {
            _triggers[triggerId.ToString()] = new TriggerInfo(triggerId, workflowId, type, entryStepOrder);
        }

        Client(pluginDll).RunTrigger(triggerId.ToString(), type, configJson);
    }

    public void CancelTrigger(string pluginDll, Guid triggerId)
    {
        Client(pluginDll).CancelTrigger(triggerId.ToString());
        lock (_triggersLock)
        {
            _triggers.Remove(triggerId.ToString());
        }
    }

    // Tear down every warm host process so the next call relaunches against the current plugin files.
    // Called on reload: a replaced plugin dll must not keep running in an old process.
    public void RecycleAll()
    {
        List<PluginClient> clients;
        lock (_clientsLock)
        {
            clients = [.. _clients.Values];
            _clients.Clear();
        }

        foreach (var client in clients)
        {
            _ = client.DisposeAsync();
        }
    }

    private readonly Dictionary<string, int> _restarts = [];

    // Snapshot of a plugin's host process for the ops surface.
    public sealed record RuntimeStatus(string State, int? Pid, DateTimeOffset? StartedAt, long MemoryBytes, int Restarts);

    public RuntimeStatus Status(string pluginDll)
    {
        lock (_clientsLock)
        {
            var restarts = _restarts.GetValueOrDefault(pluginDll);
            if (_clients.TryGetValue(pluginDll, out var client) && !client.HasExited)
            {
                return new RuntimeStatus("running", client.Pid, client.StartedAt, client.MemoryBytes, restarts);
            }

            return new RuntimeStatus(client is null ? "never-started" : "exited", null, null, 0, restarts);
        }
    }

    public IReadOnlyList<PluginLogLine> LogsSince(string pluginDll, long cursor)
    {
        lock (_clientsLock)
        {
            return _clients.TryGetValue(pluginDll, out var client) ? client.Logs.Since(cursor) : [];
        }
    }

    // One warm client per plugin; relaunch if the previous process died.
    private PluginClient Client(string pluginDll)
    {
        lock (_clientsLock)
        {
            if (_clients.TryGetValue(pluginDll, out var existing) && !existing.HasExited)
            {
                return existing;
            }

            if (existing is not null)
            {
                _ = existing.DisposeAsync();
                _restarts[pluginDll] = _restarts.GetValueOrDefault(pluginDll) + 1;
                _logger.LogWarning("Plugin host for {Plugin} had exited; relaunching", pluginDll);
            }

            var client = new PluginClient(pluginHostDll, pluginDll, this, _logger);
            _clients[pluginDll] = client;
            return client;
        }
    }

    private TriggerInfo? Lookup(string triggerId)
    {
        lock (_triggersLock)
        {
            return _triggers.GetValueOrDefault(triggerId);
        }
    }

    public void OnLog(string? source, string level, string message) =>
        _logger.Log(Enum.TryParse<LogLevel>(level, out var parsed) ? parsed : LogLevel.Information,
            "plugin[{Source}]: {Message}", source, message);

    public async Task OnFireAsync(string triggerId, string? payloadJson)
    {
        if (Lookup(triggerId) is not { } info)
        {
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        var dbContext = scope.ServiceProvider.GetRequiredService<AutomateXDbContext>();

        await bus.PublishAsync(new RunWorkflow(
            Guid.CreateVersion7(), info.WorkflowId, $"{info.Type}:{info.TriggerId}", payloadJson, info.EntryStepOrder));
        await dbContext.Triggers
            .Where(x => x.Id == info.TriggerId)
            .ExecuteUpdateAsync(x => x
                .SetProperty(t => t.LastFiredAt, DateTimeOffset.UtcNow)
                .SetProperty(t => t.LastError, (string?)null)
                .SetProperty(t => t.LastErrorAt, (DateTimeOffset?)null), CancellationToken.None);
    }

    public async Task<string?> StateGetAsync(string triggerId, string key)
    {
        if (Lookup(triggerId) is not { } info)
        {
            return null;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        return await Store(scope).GetAsync(info.WorkflowId, Scoped(info.TriggerId, key));
    }

    public async Task StateSetAsync(string triggerId, string key, string value, double? ttlSeconds)
    {
        if (Lookup(triggerId) is not { } info)
        {
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        await Store(scope).SetAsync(info.WorkflowId, Scoped(info.TriggerId, key), value, Ttl(ttlSeconds));
    }

    public async Task<bool> StateSetIfAbsentAsync(string triggerId, string key, string value, double? ttlSeconds)
    {
        if (Lookup(triggerId) is not { } info)
        {
            return false;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        return await Store(scope).SetIfAbsentAsync(info.WorkflowId, Scoped(info.TriggerId, key), value, Ttl(ttlSeconds));
    }

    public async Task<bool> StateRemoveAsync(string triggerId, string key)
    {
        if (Lookup(triggerId) is not { } info)
        {
            return false;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        return await Store(scope).RemoveAsync(info.WorkflowId, Scoped(info.TriggerId, key));
    }

    private static string Scoped(Guid triggerId, string key) => $"trigger:{triggerId}:{key}";

    private static TimeSpan? Ttl(double? seconds) => seconds is { } value ? TimeSpan.FromSeconds(value) : null;

    private static IWorkflowStateStore Store(AsyncServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<IWorkflowStateStore>();

    public async ValueTask DisposeAsync()
    {
        List<PluginClient> clients;
        lock (_clientsLock)
        {
            clients = [.. _clients.Values];
            _clients.Clear();
        }

        foreach (var client in clients)
        {
            await client.DisposeAsync();
        }
    }
}
