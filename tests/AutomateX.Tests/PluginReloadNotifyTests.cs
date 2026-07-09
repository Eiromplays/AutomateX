using System.Security.Cryptography;
using AutomateX.Database;
using AutomateX.Engine;
using AutomateX.Engine.Plugins;
using AutomateX.Engine.Triggers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;
using Xunit;

namespace AutomateX.Tests;

// v5.0 m4: a plugin reload on one node NOTIFYs peers over Postgres; every node LISTENs and reloads its own
// hosts/registries. TriggerRegistry.Generation increments on each reload, so it's the observable. The
// originating node reloads once locally and ignores its own NOTIFY (no double reload, no notify storm).
public sealed class PluginReloadNotifyTests : IAsyncLifetime
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
        });
        builder.AddAutomateXEngine(connectionString);
        return builder.Build();
    }

    [Fact]
    public async Task Reload_on_one_node_reloads_peers_and_skips_itself()
    {
        var registryA = _nodeA.Services.GetRequiredService<TriggerRegistry>();
        var registryB = _nodeB.Services.GetRequiredService<TriggerRegistry>();
        var coordinatorA = _nodeA.Services.GetRequiredService<PluginReloadCoordinator>();

        var genA0 = registryA.Generation;
        var genB0 = registryB.Generation;

        await coordinatorA.ReloadAndNotifyAsync();

        // Peer learns via NOTIFY and reloads.
        await WaitUntilAsync(() => registryB.Generation > genB0, TimeSpan.FromSeconds(20));
        Assert.True(registryB.Generation >= genB0 + 1);

        // Originating node reloaded once locally; it receives its own NOTIFY but ignores it (no second reload).
        await Task.Delay(TimeSpan.FromSeconds(2));
        Assert.Equal(genA0 + 1, registryA.Generation);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("plugin reload did not propagate within the timeout");
    }

    public async Task DisposeAsync()
    {
        await _nodeA.StopAsync();
        await _nodeB.StopAsync();
        _nodeA.Dispose();
        _nodeB.Dispose();
        await _postgres.DisposeAsync();
    }
}
