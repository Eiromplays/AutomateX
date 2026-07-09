using Npgsql;

namespace AutomateX.Engine.Plugins;

// Per-node LISTENer for cross-node plugin reloads. Holds a dedicated Postgres connection open, waits for
// NOTIFYs on the reload channel, and reloads this node's plugins when a peer signals. Reconnects on drop;
// keepalives guard against idle-timeout culls (the same guidance Wolverine gives for its long-lived
// connections). Runs on every node in every mode — the reload must reach all of them.
public sealed class PluginReloadListener(
    string connectionString,
    PluginReloadCoordinator coordinator,
    ILogger<PluginReloadListener> logger) : BackgroundService
{
    private readonly string _connectionString = new NpgsqlConnectionStringBuilder(connectionString)
    {
        KeepAlive = 30,
        TcpKeepAlive = true,
    }.ConnectionString;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ListenAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Plugin reload listener connection dropped; reconnecting");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    private async Task ListenAsync(CancellationToken stoppingToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(stoppingToken);

        connection.Notification += (_, e) =>
        {
            try
            {
                coordinator.ApplyPeerSignal(e.Payload);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Applying a peer plugin-reload signal failed");
            }
        };

        // Channel is a compile-time constant, not user input — safe to inline into LISTEN.
        await using (var command = new NpgsqlCommand($"LISTEN {PluginReloadCoordinator.Channel}", connection))
        {
            await command.ExecuteNonQueryAsync(stoppingToken);
        }

        logger.LogInformation(
            "Listening for cross-node plugin reloads on '{Channel}'", PluginReloadCoordinator.Channel);

        while (!stoppingToken.IsCancellationRequested)
        {
            await connection.WaitAsync(stoppingToken);
        }
    }
}
