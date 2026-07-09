using AutomateX.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AutomateX.Engine.Plugins;

// Cross-node plugin reload (v5.0). An originating reload (upload/install/delete/enable-toggle) recycles this
// node's plugin hosts and rebuilds its registries, then NOTIFYs peers over Postgres so every node does the
// same. Peer-triggered reloads never re-notify (that would storm the cluster), and a node ignores its own
// NOTIFY (Postgres also delivers a notification to the sender's other sessions).
public sealed class PluginReloadCoordinator(PluginReloader reloader, IServiceScopeFactory scopeFactory)
{
    public const string Channel = "automatex_plugins_changed";

    // Per-process id, stamped on every NOTIFY this node sends so its own listener can ignore it.
    public string InstanceId { get; } = Guid.CreateVersion7().ToString();

    // Originating reload: reload locally, then tell peers.
    public async Task<ReloadResult> ReloadAndNotifyAsync(CancellationToken cancellationToken = default)
    {
        var result = reloader.Reload();

        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AutomateXDbContext>();
        // pg_notify (not NOTIFY) so the channel + payload can be parameters, not literals.
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_notify({Channel}, {InstanceId})", cancellationToken);

        return result;
    }

    // Peer signal from the LISTENer: reload only (never re-notify), and skip our own NOTIFY.
    public void ApplyPeerSignal(string? payload)
    {
        if (payload == InstanceId)
        {
            return;
        }

        reloader.Reload();
    }
}
