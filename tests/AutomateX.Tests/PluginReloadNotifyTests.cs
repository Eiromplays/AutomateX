using System.Security.Cryptography;
using AutomateX.Database;
using AutomateX.Engine;
using AutomateX.Engine.Plugins;
using AutomateX.Engine.Triggers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
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

        var genA = registryA.Generation;
        var genB = registryB.Generation;

        // Reload node A once locally; this also NOTIFYs peers.
        await coordinatorA.ReloadAndNotifyAsync();

        // A NOTIFY sent before a peer's listener has issued LISTEN is lost (notifications aren't durable),
        // and the listener starts asynchronously — under a slow/contended CI runner it may not be ready for
        // that first signal. Re-send node A's signal (raw, so node A is NOT reloaded again — its own listener
        // keeps skipping its id) until node B picks it up.
        await WaitUntilAsync(async () =>
        {
            if (registryB.Generation > genB)
            {
                return true;
            }

            await NotifyRawAsync(coordinatorA.InstanceId);
            await Task.Delay(200);
            return registryB.Generation > genB;
        }, TimeSpan.FromSeconds(30));

        Assert.True(registryB.Generation >= genB + 1);

        // Node A reloaded once locally and ignores its own NOTIFY (and every re-send) — no second reload.
        await Task.Delay(TimeSpan.FromSeconds(1));
        Assert.Equal(genA + 1, registryA.Generation);
    }

    // Raw pg_notify from an independent connection — used to re-send the origin's signal without reloading it.
    private async Task NotifyRawAsync(string payload)
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"SELECT pg_notify('{PluginReloadCoordinator.Channel}', @payload)", connection);
        command.Parameters.AddWithValue("payload", payload);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await condition())
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
