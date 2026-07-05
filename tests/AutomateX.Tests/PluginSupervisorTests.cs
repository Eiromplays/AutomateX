using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;
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
        if (!OutOfProcGate.Ready(hostDll, pluginDll, output))
        {
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

    [Fact]
    public async Task RecycleAll_drops_warm_processes_and_relaunches_on_next_call()
    {
        var (hostDll, pluginDll) = Locate();
        if (!OutOfProcGate.Ready(hostDll, pluginDll, output))
        {
            return;
        }

        var supervisor = new PluginProcessSupervisor(
            fixture.Host.Services.GetRequiredService<IServiceScopeFactory>(),
            fixture.Host.Services.GetRequiredService<ILoggerFactory>(),
            hostDll);

        await using (supervisor)
        {
            supervisor.RecycleAll(); // no warm clients yet — must be a safe no-op

            var before = await supervisor.DescribeAsync(pluginDll);
            Assert.NotNull(before["result"]?["actions"]);

            supervisor.RecycleAll(); // tear the warm process down

            // The next call relaunches a fresh host and still describes correctly.
            var after = await supervisor.DescribeAsync(pluginDll);
            Assert.NotNull(after["result"]?["actions"]);
        }
    }

    // v4.7: Engine:BlockPrivateNetworkRequests flows through the supervisor to the child's env, so an
    // out-of-proc plugin's HTTP obeys the egress guard. Same loopback URL for all supervisors — only the
    // option differs — so the 200-vs-throw contrast isolates the guard as the cause. v4.8: a supervisor
    // with the guard on but the loopback host on EgressAllowlist reaches it again — the allowlist widens
    // end-to-end through the same supervisor → client → child-env path.
    [Fact]
    public async Task Egress_guard_blocks_plugin_http_when_the_option_is_on()
    {
        var (hostDll, pluginDll) = Locate();
        if (!OutOfProcGate.Ready(hostDll, pluginDll, output))
        {
            return;
        }

        using var listener = new HttpListener();
        var url = $"http://127.0.0.1:{FreeTcpPort()}/";
        listener.Prefixes.Add(url);
        try
        {
            listener.Start();
        }
        catch (HttpListenerException ex)
        {
            output.WriteLine($"Skipped — cannot bind a loopback listener here: {ex.Message}");
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                while (listener.IsListening)
                {
                    var context = await listener.GetContextAsync();
                    context.Response.StatusCode = 200;
                    context.Response.Close();
                }
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException)
            {
                // listener disposed at test end
            }
        });

        var config = new JsonObject { ["url"] = url }.ToJsonString();
        var scopeFactory = fixture.Host.Services.GetRequiredService<IServiceScopeFactory>();
        var loggerFactory = fixture.Host.Services.GetRequiredService<ILoggerFactory>();

        await using (var permitted = new PluginProcessSupervisor(scopeFactory, loggerFactory, hostDll))
        {
            var result = await permitted.ExecuteActionAsync(pluginDll, "sample.httpget", config);
            Assert.Contains("\"statusCode\":200", result);
        }

        await using (var blocked = new PluginProcessSupervisor(
            scopeFactory, loggerFactory, hostDll, blockPrivateEgress: true))
        {
            await Assert.ThrowsAsync<PluginHostException>(
                () => blocked.ExecuteActionAsync(pluginDll, "sample.httpget", config));
        }

        await using (var allowlisted = new PluginProcessSupervisor(
            scopeFactory, loggerFactory, hostDll, blockPrivateEgress: true, egressAllowlist: ["127.0.0.1"]))
        {
            var result = await allowlisted.ExecuteActionAsync(pluginDll, "sample.httpget", config);
            Assert.Contains("\"statusCode\":200", result);
        }
    }

    private static int FreeTcpPort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        try
        {
            return ((IPEndPoint)probe.LocalEndpoint).Port;
        }
        finally
        {
            probe.Stop();
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
