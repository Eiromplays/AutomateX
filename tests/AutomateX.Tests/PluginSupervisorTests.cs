using AutomateX.Database;
using AutomateX.Engine.Plugins;
using AutomateX.Modules.Triggers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace AutomateX.Tests;

// v4.0: the supervisor runs a plugin trigger out-of-process and its fire callback lands as a real
// RunWorkflow — proving fire → engine and the warm-process wiring. Skips if binaries aren't built.
public sealed class PluginSupervisorTests(EngineFixture fixture, ITestOutputHelper output)
    : IClassFixture<EngineFixture>
{
    [Fact]
    public async Task Trigger_fire_enqueues_a_workflow_run()
    {
        var (hostDll, pluginDll) = Locate();
        if (hostDll is null || pluginDll is null)
        {
            output.WriteLine("Skipped — build the solution first.");
            return;
        }

        var workflowId = await TestData.SeedWorkflowAsync(fixture.Host, stepCount: 1);
        Guid triggerId;
        await using (var scope = fixture.Host.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AutomateXDbContext>();
            var trigger = Trigger.Create(workflowId, "sample.ticker", """{"intervalMilliseconds":30,"maxFires":2}""", null);
            dbContext.Triggers.Add(trigger);
            await dbContext.SaveChangesAsync();
            triggerId = trigger.Id;
        }

        var supervisor = new PluginProcessSupervisor(
            fixture.Host.Services.GetRequiredService<IServiceScopeFactory>(),
            fixture.Host.Services.GetRequiredService<ILoggerFactory>(),
            hostDll);

        await using (supervisor)
        {
            supervisor.RunTrigger(pluginDll, triggerId, "sample.ticker", workflowId, entryStepOrder: null,
                """{"intervalMilliseconds":30,"maxFires":2}""");

            var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
            var ran = false;
            while (DateTimeOffset.UtcNow < deadline)
            {
                await using var scope = fixture.Host.Services.CreateAsyncScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<AutomateXDbContext>();
                if (await dbContext.Executions.AsNoTracking().AnyAsync(x => x.WorkflowId == workflowId))
                {
                    ran = true;
                    break;
                }

                await Task.Delay(100);
            }

            supervisor.CancelTrigger(pluginDll, triggerId);
            Assert.True(ran, "the ticker's fire never produced a workflow run");
        }
    }

    private static (string? HostDll, string? PluginDll) Locate()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AutomateX.slnx")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            return (null, null);
        }

        var config = AppContext.BaseDirectory.Contains(
            $"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            ? "Release"
            : "Debug";
        const string tfm = "net10.0";

        var host = Path.Combine(dir.FullName, "src", "AutomateX.PluginHost", "bin", config, tfm, "AutomateX.PluginHost.dll");
        var plugin = Path.Combine(dir.FullName, "samples", "AutomateX.SamplePlugin", "bin", config, tfm, "AutomateX.SamplePlugin.dll");
        return File.Exists(host) && File.Exists(plugin) ? (host, plugin) : (null, null);
    }
}
