using AutomateX.Engine.Actions;
using AutomateX.Engine.Plugins;
using AutomateX.Modules.Executions;
using AutomateX.Modules.Workspaces;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutomateX.Tests;

// Hot-reload must never lose host/builtin actions, and the engine must keep
// executing across a swap.
public sealed class PluginReloadTests(EngineFixture fixture) : IClassFixture<EngineFixture>
{
    [Fact]
    public async Task Rebuild_preserves_host_actions_and_workflows_still_run()
    {
        fixture.ProbeAction.Reset();
        var registry = fixture.Host.Services.GetRequiredService<ActionRegistry>();

        registry.Rebuild();

        Assert.True(registry.Contains("test.probe", Workspace.DefaultId));
        Assert.True(registry.Contains("http.request", Workspace.DefaultId));

        var workflowId = await TestData.SeedWorkflowAsync(fixture.Host, stepCount: 1);
        var execution = await TestData.WaitForTerminalAsync(
            fixture.Host, await TestData.ExecuteAsync(fixture.Host, workflowId), TimeSpan.FromSeconds(20));

        Assert.Equal(ExecutionStatus.Succeeded, execution.Status);
    }

    [Fact]
    public void Full_reload_swaps_assemblies_registry_and_bus_without_losing_actions()
    {
        var reloader = fixture.Host.Services.GetRequiredService<PluginReloader>();
        var registry = fixture.Host.Services.GetRequiredService<ActionRegistry>();

        var result = reloader.Reload();

        Assert.True(result.GlobalPlugins >= 0);
        Assert.True(registry.Contains("test.probe", Workspace.DefaultId));
        Assert.Contains(registry.Descriptors(Workspace.DefaultId), x => x.Type == "http.request");
    }
}
