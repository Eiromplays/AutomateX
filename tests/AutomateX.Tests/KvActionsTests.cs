using AutomateX.Engine.Actions;
using AutomateX.Modules.State;
using AutomateX.Plugin.Sdk;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AutomateX.Tests;

// The kv.* built-ins over the durable per-workflow store: roundtrip, the setIfAbsent dedup
// primitive, per-workflow isolation, and the "kv:" namespacing that keeps them clear of a
// trigger's internal state keys.
public sealed class KvActionsTests
{
    private static (IServiceScopeFactory ScopeFactory, FakeStateStore Store) Build()
    {
        var store = new FakeStateStore();
        var services = new ServiceCollection().AddSingleton<IWorkflowStateStore>(store).BuildServiceProvider();
        return (services.GetRequiredService<IServiceScopeFactory>(), store);
    }

    private static ActionContext Context(Guid workflowId) => new()
    {
        Logger = NullLogger.Instance,
        Http = new HttpClient(),
        ExecutionId = Guid.CreateVersion7(),
        WorkflowId = workflowId,
        StepOrder = 0,
    };

    [Fact]
    public async Task Set_then_get_roundtrips()
    {
        var (scopeFactory, _) = Build();
        var ctx = Context(Guid.CreateVersion7());

        await new KvSetAction(scopeFactory).ExecuteAsync(new KvSetConfig("greeting", "hi"), ctx);
        var got = await new KvGetAction(scopeFactory).ExecuteAsync(new KvGetConfig("greeting"), ctx);

        Assert.True(got.Found);
        Assert.Equal("hi", got.Value);
    }

    [Fact]
    public async Task Get_missing_key_reports_not_found()
    {
        var (scopeFactory, _) = Build();

        var got = await new KvGetAction(scopeFactory).ExecuteAsync(new KvGetConfig("nope"), Context(Guid.CreateVersion7()));

        Assert.False(got.Found);
        Assert.Null(got.Value);
    }

    [Fact]
    public async Task SetIfAbsent_acquires_once_then_refuses()
    {
        var (scopeFactory, _) = Build();
        var ctx = Context(Guid.CreateVersion7());
        var action = new KvSetIfAbsentAction(scopeFactory);

        var first = await action.ExecuteAsync(new KvSetIfAbsentConfig("deployed:v1"), ctx);
        var second = await action.ExecuteAsync(new KvSetIfAbsentConfig("deployed:v1"), ctx);

        Assert.True(first.Acquired);
        Assert.False(second.Acquired);
    }

    [Fact]
    public async Task Delete_removes_a_live_key()
    {
        var (scopeFactory, _) = Build();
        var ctx = Context(Guid.CreateVersion7());
        await new KvSetAction(scopeFactory).ExecuteAsync(new KvSetConfig("k", "v"), ctx);

        var removed = await new KvDeleteAction(scopeFactory).ExecuteAsync(new KvDeleteConfig("k"), ctx);
        var got = await new KvGetAction(scopeFactory).ExecuteAsync(new KvGetConfig("k"), ctx);

        Assert.True(removed.Removed);
        Assert.False(got.Found);
    }

    [Fact]
    public async Task State_is_scoped_per_workflow()
    {
        var (scopeFactory, _) = Build();
        var a = Context(Guid.CreateVersion7());
        var b = Context(Guid.CreateVersion7());

        await new KvSetAction(scopeFactory).ExecuteAsync(new KvSetConfig("shared", "fromA"), a);
        var fromB = await new KvGetAction(scopeFactory).ExecuteAsync(new KvGetConfig("shared"), b);

        Assert.False(fromB.Found);
    }

    [Fact]
    public async Task Keys_are_namespaced_under_kv()
    {
        var (scopeFactory, store) = Build();
        var wf = Guid.CreateVersion7();

        await new KvSetAction(scopeFactory).ExecuteAsync(new KvSetConfig("greeting", "hi"), Context(wf));

        Assert.Contains((wf, "kv:greeting"), store.Keys);
    }

    [Fact]
    public async Task Empty_key_is_rejected()
    {
        var (scopeFactory, _) = Build();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            new KvGetAction(scopeFactory).ExecuteAsync(new KvGetConfig(" "), Context(Guid.CreateVersion7())));
    }

    private sealed class FakeStateStore : IWorkflowStateStore
    {
        private readonly Dictionary<(Guid, string), string> _store = new();

        public IReadOnlyCollection<(Guid, string)> Keys => _store.Keys.ToList();

        public Task<string?> GetAsync(Guid workflowId, string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(_store.TryGetValue((workflowId, key), out var v) ? v : null);

        public Task SetAsync(Guid workflowId, string key, string value, TimeSpan? ttl = null, CancellationToken cancellationToken = default)
        {
            _store[(workflowId, key)] = value;
            return Task.CompletedTask;
        }

        public Task<bool> SetIfAbsentAsync(Guid workflowId, string key, string value, TimeSpan? ttl = null, CancellationToken cancellationToken = default)
        {
            if (_store.ContainsKey((workflowId, key)))
            {
                return Task.FromResult(false);
            }

            _store[(workflowId, key)] = value;
            return Task.FromResult(true);
        }

        public Task<bool> RemoveAsync(Guid workflowId, string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(_store.Remove((workflowId, key)));

        public Task<int> ClearAsync(Guid workflowId, CancellationToken cancellationToken = default)
        {
            var keys = _store.Keys.Where(k => k.Item1 == workflowId).ToList();
            foreach (var k in keys)
            {
                _store.Remove(k);
            }

            return Task.FromResult(keys.Count);
        }

        public Task<int> ClearByPrefixAsync(Guid workflowId, string prefix, CancellationToken cancellationToken = default)
        {
            var keys = _store.Keys
                .Where(k => k.Item1 == workflowId && k.Item2.StartsWith(prefix, StringComparison.Ordinal))
                .ToList();
            foreach (var k in keys)
            {
                _store.Remove(k);
            }

            return Task.FromResult(keys.Count);
        }

        public Task<IReadOnlyList<WorkflowStateItem>> ListByPrefixAsync(Guid workflowId, string prefix, CancellationToken cancellationToken = default)
        {
            var items = _store
                .Where(kv => kv.Key.Item1 == workflowId && kv.Key.Item2.StartsWith(prefix, StringComparison.Ordinal))
                .Select(kv => new WorkflowStateItem(kv.Key.Item2, kv.Value, null, DateTimeOffset.UtcNow))
                .ToList();
            return Task.FromResult<IReadOnlyList<WorkflowStateItem>>(items);
        }
    }
}
