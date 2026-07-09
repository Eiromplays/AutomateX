using System.Security.Cryptography;
using AutomateX.Database;
using AutomateX.Engine;
using AutomateX.Engine.Actions;
using AutomateX.Engine.Triggers;
using AutomateX.Modules.Triggers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;
using Xunit;

namespace AutomateX.Tests;

// v5.0 m3: PluginTriggerHost is a SingularAgent, so on a two-node cluster leader election assigns it to
// exactly one node. test.echoConfig fires once per listener start and then parks, so a listener started on
// both nodes would produce two executions. Exactly one proves the host runs on a single node only.
public sealed class MultiNodeAgentTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private IHost _nodeA = null!;
    private IHost _nodeB = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        var connectionString = _postgres.GetConnectionString();

        _nodeA = BuildNode(connectionString);
        _nodeB = BuildNode(connectionString);

        await using (var scope = _nodeA.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AutomateXDbContext>();
            await dbContext.Database.EnsureCreatedAsync();
            await WorkspaceBootstrap.EnsureDefaultAsync(dbContext);
        }

        await _nodeA.StartAsync();
        await _nodeB.StartAsync();
    }

    private static IHost BuildNode(string connectionString)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Encryption:Key"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            ["Engine:DurabilityMode"] = "Balanced",
            ["Engine:ExecutionQueuePollInterval"] = "00:00:00.250",
        });
        builder.AddAutomateXEngine(
            connectionString, options => options.TriggerSyncInterval = TimeSpan.FromMilliseconds(500));
        builder.Services.AddSingleton<IActionExecutor>(new NoopProbe());
        // Makes test.echoConfig (defined in this assembly) a live trigger type on both nodes.
        builder.Services.AddSingleton<ITriggerSource>(sp => new AssemblyTriggerSource(typeof(EngineFixture).Assembly, sp));
        return builder.Build();
    }

    [Fact]
    public async Task Plugin_trigger_listener_runs_on_one_node_only()
    {
        var workflowId = await TestData.SeedWorkflowAsync(_nodeA, stepCount: 1);

        Guid triggerId;
        await using (var scope = _nodeA.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AutomateXDbContext>();
            var trigger = Trigger.Create(workflowId, "test.echoConfig", """{"secret":"s3cr3t"}""", null);
            dbContext.Triggers.Add(trigger);
            await dbContext.SaveChangesAsync();
            triggerId = trigger.Id;
        }

        var triggeredBy = $"test.echoConfig:{triggerId}";

        // The agent is assigned to one node by leader election; wait for its single listener to fire.
        await WaitForAtLeastAsync(triggeredBy, 1, TimeSpan.FromSeconds(40));

        // If the host had (wrongly) started on both nodes, the second listener's fire lands in this window.
        await Task.Delay(TimeSpan.FromSeconds(5));
        Assert.Equal(1, await CountAsync(triggeredBy));
    }

    private async Task WaitForAtLeastAsync(string triggeredBy, int count, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await CountAsync(triggeredBy) >= count)
            {
                return;
            }

            await Task.Delay(250);
        }

        throw new TimeoutException($"trigger {triggeredBy} did not fire within {timeout}");
    }

    private async Task<int> CountAsync(string triggeredBy)
    {
        await using var scope = _nodeA.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AutomateXDbContext>();
        return await dbContext.Executions.AsNoTracking().CountAsync(x => x.TriggeredBy == triggeredBy);
    }

    public async Task DisposeAsync()
    {
        await _nodeA.StopAsync();
        await _nodeB.StopAsync();
        _nodeA.Dispose();
        _nodeB.Dispose();
        await _postgres.DisposeAsync();
    }

    private sealed class NoopProbe : IActionExecutor
    {
        public string ActionType => "test.probe";

        public Task<string?> ExecuteAsync(
            string configJson, ActionInvocation invocation, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>("ok");
    }
}
