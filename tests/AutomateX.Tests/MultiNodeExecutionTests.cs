using System.Security.Cryptography;
using AutomateX.Database;
using AutomateX.Engine;
using AutomateX.Engine.Actions;
using AutomateX.Modules.Executions;
using AutomateX.Modules.Workspaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;
using Xunit;

namespace AutomateX.Tests;

// v5.0 m2: in Balanced mode the execution lane rides a shared Postgres queue (FOR UPDATE SKIP LOCKED), so two
// nodes on one database are competing consumers — a published RunWorkflow is claimed and run by exactly one
// node, never both. Solo mode keeps fast in-proc local queues (exercised by the rest of the suite).
public sealed class MultiNodeExecutionTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    // One shared instance registered into both nodes, so the call count is cluster-wide.
    private readonly CountingAction _action = new();
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

        // Wolverine auto-provisions its own message-store + queue schema on start.
        await _nodeA.StartAsync();
        await _nodeB.StartAsync();
    }

    private IHost BuildNode(string connectionString)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Encryption:Key"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            ["Engine:DurabilityMode"] = "Balanced",
            // Poll fast so a two-node run settles quickly under test.
            ["Engine:ExecutionQueuePollInterval"] = "00:00:00.250",
        });
        builder.AddAutomateXEngine(connectionString);
        builder.Services.AddSingleton<IActionExecutor>(_action);
        return builder.Build();
    }

    [Fact]
    public async Task Two_nodes_process_a_run_exactly_once()
    {
        var workflowId = await TestData.SeedWorkflowAsync(_nodeA, stepCount: 1);
        var executionId = await TestData.ExecuteAsync(_nodeA, workflowId);

        var execution = await TestData.WaitForCompletedAsync(_nodeA, executionId, TimeSpan.FromSeconds(30));

        Assert.Equal(ExecutionStatus.Succeeded, execution.Status);
        Assert.Equal(1, _action.Calls);
    }

    public async Task DisposeAsync()
    {
        await _nodeA.StopAsync();
        await _nodeB.StopAsync();
        _nodeA.Dispose();
        _nodeB.Dispose();
        await _postgres.DisposeAsync();
    }

    private sealed class CountingAction : IActionExecutor
    {
        private int _calls;

        public int Calls => _calls;

        public string ActionType => "test.probe";

        public Task<string?> ExecuteAsync(
            string configJson, ActionInvocation invocation, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult<string?>("ok");
        }
    }
}
