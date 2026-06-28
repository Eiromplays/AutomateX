using System.Text.Json;
using AutomateX.Engine;
using AutomateX.Modules.Workspaces;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutomateX.Tests;

// The opt-in real single-step run: executes one leaf action with a resolved config, rejects
// control-flow nodes, and surfaces resolution/execution errors instead of throwing.
public sealed class StepTestRunnerTests(EngineFixture fixture) : IClassFixture<EngineFixture>
{
    private static JsonElement El(string json) => JsonSerializer.Deserialize<JsonElement>(json);

    private async Task<StepTestResult> RunAsync(
        string actionType, string configJson, JsonElement? triggerPayload = null)
    {
        await using var scope = fixture.Host.Services.CreateAsyncScope();
        var runner = scope.ServiceProvider.GetRequiredService<StepTestRunner>();
        return await runner.RunAsync(
            Workspace.DefaultId,
            Guid.CreateVersion7(),
            actionType,
            configJson,
            new Dictionary<string, int>(),
            triggerPayload,
            new Dictionary<int, JsonElement>(),
            CancellationToken.None);
    }

    [Fact]
    public async Task Runs_a_leaf_action_and_returns_its_output()
    {
        fixture.ProbeAction.Reset();

        var result = await RunAsync("test.probe", """{"v":"{{trigger.payload.v}}"}""", El("""{"v":"hi"}"""));

        Assert.True(result.Ok);
        Assert.Null(result.Error);
        Assert.NotNull(result.Output);
        Assert.Equal(1, fixture.ProbeAction.Calls); // ran exactly once
    }

    [Fact]
    public async Task Rejects_control_flow_nodes_without_running_them()
    {
        var result = await RunAsync("switch", "{}");

        Assert.False(result.Ok);
        Assert.Contains("control-flow", result.Error);
    }

    [Fact]
    public async Task Unknown_action_is_an_error_not_a_throw()
    {
        var result = await RunAsync("does.not.exist", "{}");

        Assert.False(result.Ok);
        Assert.Contains("Unknown action", result.Error);
    }

    [Fact]
    public async Task Unresolved_reference_fails_before_the_action_runs()
    {
        fixture.ProbeAction.Reset();

        var result = await RunAsync("test.probe", """{"v":"{{trigger.payload.missing}}"}""");

        Assert.False(result.Ok);
        Assert.NotNull(result.Error);
        Assert.Equal(0, fixture.ProbeAction.Calls);
    }
}
