using AutomateX.Modules.State;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutomateX.Tests;

// The durable "remember between runs" primitive (v3 milestone 2). Rules pinned
// here before any consumer exists:
//  - Set/Get round-trips; Set overwrites; a missing key reads null.
//  - SetIfAbsent is the dedup primitive: true exactly once for a fresh key,
//    false (and NO overwrite) while a live entry exists — so "never re-fire a
//    recorded id" holds even under concurrent fires.
//  - Expiry: an expired entry reads as absent and SetIfAbsent reclaims it.
//  - ListByPrefix returns a key namespace, excluding expired entries.
//  - State is owned by, and isolated to, its workflow.
public sealed class WorkflowStateTests(EngineFixture fixture) : IClassFixture<EngineFixture>
{
    private static readonly TimeSpan Expired = TimeSpan.FromSeconds(-1);

    private async Task<T> WithStoreAsync<T>(Func<IWorkflowStateStore, Task<T>> body)
    {
        await using var scope = fixture.Host.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IWorkflowStateStore>();
        return await body(store);
    }

    [Fact]
    public async Task Set_then_get_round_trips()
    {
        var wf = await TestData.SeedWorkflowAsync(fixture.Host, 1);

        var value = await WithStoreAsync(async store =>
        {
            await store.SetAsync(wf, "k", "v1");
            return await store.GetAsync(wf, "k");
        });

        Assert.Equal("v1", value);
    }

    [Fact]
    public async Task Set_overwrites_existing_value()
    {
        var wf = await TestData.SeedWorkflowAsync(fixture.Host, 1);

        var value = await WithStoreAsync(async store =>
        {
            await store.SetAsync(wf, "k", "v1");
            await store.SetAsync(wf, "k", "v2");
            return await store.GetAsync(wf, "k");
        });

        Assert.Equal("v2", value);
    }

    [Fact]
    public async Task Missing_key_reads_null()
    {
        var wf = await TestData.SeedWorkflowAsync(fixture.Host, 1);

        var value = await WithStoreAsync(store => store.GetAsync(wf, "nope"));

        Assert.Null(value);
    }

    [Fact]
    public async Task SetIfAbsent_is_true_once_then_false_without_overwriting()
    {
        var wf = await TestData.SeedWorkflowAsync(fixture.Host, 1);

        var (first, second, stored) = await WithStoreAsync(async store =>
        {
            var a = await store.SetIfAbsentAsync(wf, "seen:1", "first");
            var b = await store.SetIfAbsentAsync(wf, "seen:1", "second");
            return (a, b, await store.GetAsync(wf, "seen:1"));
        });

        Assert.True(first);
        Assert.False(second);
        Assert.Equal("first", stored); // the live entry is never clobbered
    }

    [Fact]
    public async Task Expired_entry_reads_as_absent()
    {
        var wf = await TestData.SeedWorkflowAsync(fixture.Host, 1);

        var value = await WithStoreAsync(async store =>
        {
            await store.SetAsync(wf, "k", "v", ttl: Expired);
            return await store.GetAsync(wf, "k");
        });

        Assert.Null(value);
    }

    [Fact]
    public async Task Live_ttl_entry_is_still_readable()
    {
        var wf = await TestData.SeedWorkflowAsync(fixture.Host, 1);

        var value = await WithStoreAsync(async store =>
        {
            await store.SetAsync(wf, "k", "v", ttl: TimeSpan.FromHours(1));
            return await store.GetAsync(wf, "k");
        });

        Assert.Equal("v", value);
    }

    [Fact]
    public async Task SetIfAbsent_reclaims_an_expired_entry()
    {
        var wf = await TestData.SeedWorkflowAsync(fixture.Host, 1);

        var (reclaimed, value) = await WithStoreAsync(async store =>
        {
            await store.SetAsync(wf, "seen:1", "old", ttl: Expired);
            var r = await store.SetIfAbsentAsync(wf, "seen:1", "new");
            return (r, await store.GetAsync(wf, "seen:1"));
        });

        Assert.True(reclaimed); // expired == absent, so the slot is free
        Assert.Equal("new", value);
    }

    [Fact]
    public async Task ListByPrefix_returns_namespace_excluding_expired()
    {
        var wf = await TestData.SeedWorkflowAsync(fixture.Host, 1);

        var keys = await WithStoreAsync(async store =>
        {
            await store.SetAsync(wf, "seen:a", "1");
            await store.SetAsync(wf, "seen:b", "2");
            await store.SetAsync(wf, "seen:gone", "3", ttl: Expired);
            await store.SetAsync(wf, "other:c", "4");
            var items = await store.ListByPrefixAsync(wf, "seen:");
            return items.Select(x => x.Key).ToList();
        });

        Assert.Equal(new[] { "seen:a", "seen:b" }, keys);
    }

    [Fact]
    public async Task Remove_deletes_then_reports_absence()
    {
        var wf = await TestData.SeedWorkflowAsync(fixture.Host, 1);

        var (firstRemove, afterValue, secondRemove) = await WithStoreAsync(async store =>
        {
            await store.SetAsync(wf, "k", "v");
            var removed = await store.RemoveAsync(wf, "k");
            var value = await store.GetAsync(wf, "k");
            var removedAgain = await store.RemoveAsync(wf, "k");
            return (removed, value, removedAgain);
        });

        Assert.True(firstRemove);
        Assert.Null(afterValue);
        Assert.False(secondRemove);
    }

    [Fact]
    public async Task State_is_isolated_per_workflow()
    {
        var wf1 = await TestData.SeedWorkflowAsync(fixture.Host, 1);
        var wf2 = await TestData.SeedWorkflowAsync(fixture.Host, 1);

        var crossRead = await WithStoreAsync(async store =>
        {
            await store.SetAsync(wf1, "k", "v");
            return await store.GetAsync(wf2, "k");
        });

        Assert.Null(crossRead);
    }
}
