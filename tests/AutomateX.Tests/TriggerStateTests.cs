using AutomateX.Engine.Triggers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutomateX.Tests;

// The engine-backed trigger state (real WorkflowState store) auto-namespaces keys
// per trigger, so two triggers on one workflow can't collide on a bare key — and
// dedup within one trigger holds across calls.
public sealed class TriggerStateTests(EngineFixture fixture) : IClassFixture<EngineFixture>
{
    private WorkflowScopedTriggerState StateFor(Guid workflowId, Guid triggerId) =>
        new(fixture.Host.Services.GetRequiredService<IServiceScopeFactory>(), workflowId, triggerId);

    [Fact]
    public async Task SetIfAbsent_dedups_within_a_trigger()
    {
        var workflowId = await TestData.SeedWorkflowAsync(fixture.Host, 1);
        var state = StateFor(workflowId, Guid.CreateVersion7());

        Assert.True(await state.SetIfAbsentAsync("seen:1", "x"));
        Assert.False(await state.SetIfAbsentAsync("seen:1", "y"));
        Assert.Equal("x", await state.GetAsync("seen:1"));
    }

    [Fact]
    public async Task Two_triggers_on_one_workflow_do_not_collide()
    {
        var workflowId = await TestData.SeedWorkflowAsync(fixture.Host, 1);
        var first = StateFor(workflowId, Guid.CreateVersion7());
        var second = StateFor(workflowId, Guid.CreateVersion7());

        // Same bare key, different triggers -> both see it as new (separate namespaces).
        Assert.True(await first.SetIfAbsentAsync("seen:1", "a"));
        Assert.True(await second.SetIfAbsentAsync("seen:1", "b"));
        Assert.Null(await second.GetAsync("other"));
    }
}
