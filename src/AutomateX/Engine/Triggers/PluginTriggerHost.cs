using System.Text.Json;
using AutomateX.Database;
using AutomateX.Engine.Security;
using AutomateX.Engine.Templating;
using AutomateX.Modules.Triggers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Wolverine;

namespace AutomateX.Engine.Triggers;

// Supervises one listener loop per enabled plugin-trigger row. Sync runs on an
// interval: rows that appeared start, rows that vanished (or changed config, or
// whose code was hot-reloaded — the registry generation) stop and restart fresh.
// A listener that returns is re-run after a short delay (polling pattern); one
// that throws restarts with backoff.
public sealed class PluginTriggerHost(
    IServiceScopeFactory scopeFactory,
    TriggerRegistry registry,
    IOptions<EngineOptions> engineOptions,
    ILogger<PluginTriggerHost> logger) : BackgroundService
{
    private sealed record Runner(CancellationTokenSource Cts, Task Task, string Fingerprint);

    private readonly Dictionary<Guid, Runner> _runners = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SyncAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Plugin trigger sync failed; retrying next cycle");
            }

            try
            {
                await Task.Delay(engineOptions.Value.TriggerSyncInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        foreach (var runner in _runners.Values)
        {
            runner.Cts.Cancel();
        }
    }

    private async Task SyncAsync(CancellationToken stoppingToken)
    {
        var types = registry.Types;
        List<(Guid Id, string Type, string ConfigJson, Guid WorkflowId)> wanted = [];

        if (types.Count > 0)
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AutomateXDbContext>();
            wanted = (await dbContext.Triggers
                    .AsNoTracking()
                    .Where(x => x.Enabled && types.Contains(x.Type))
                    .Select(x => new { x.Id, x.Type, x.ConfigJson, x.WorkflowId })
                    .ToListAsync(stoppingToken))
                .Select(x => (x.Id, x.Type, x.ConfigJson, x.WorkflowId))
                .ToList();
        }

        var generation = registry.Generation;
        var wantedById = wanted.ToDictionary(
            x => x.Id,
            x => (Row: x, Fingerprint: $"{x.Type}|{x.ConfigJson}|{generation}"));

        foreach (var (id, runner) in _runners.ToList())
        {
            if (!wantedById.TryGetValue(id, out var entry) || entry.Fingerprint != runner.Fingerprint)
            {
                runner.Cts.Cancel();
                _runners.Remove(id);
                logger.LogInformation("Stopped plugin trigger listener {TriggerId}", id);
            }
        }

        foreach (var (id, entry) in wantedById)
        {
            if (_runners.ContainsKey(id))
            {
                continue;
            }

            var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            var task = Task.Run(() => RunListenerLoopAsync(entry.Row, cts.Token), CancellationToken.None);
            _runners[id] = new Runner(cts, task, entry.Fingerprint);
            logger.LogInformation(
                "Started plugin trigger listener {TriggerId} ({TriggerType})", id, entry.Row.Type);
        }
    }

    private async Task RunListenerLoopAsync(
        (Guid Id, string Type, string ConfigJson, Guid WorkflowId) row, CancellationToken cancellationToken)
    {
        var failures = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var runner = registry.CreateRunner(row.Type);
                if (runner is null)
                {
                    return; // type vanished (plugin removed); sync will clean us up
                }

                var context = new TriggerRunnerContext(row.Id, row.WorkflowId,
                    payload => FireAsync(row, payload, cancellationToken));

                // Trigger configs support {{connections.<name>.<field>}} — resolved
                // fresh at every listener start; the stored row keeps the template.
                var configJson = row.ConfigJson.Contains("{{", StringComparison.Ordinal)
                    ? await ResolveConfigAsync(row.WorkflowId, row.ConfigJson, cancellationToken)
                    : row.ConfigJson;

                await runner.RunAsync(configJson, context, cancellationToken);
                failures = 0; // clean return = poll cycle done
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                failures++;
                logger.LogError(ex,
                    "Trigger listener {TriggerType} ({TriggerId}) crashed (failure {Failures}), backing off",
                    row.Type, row.Id, failures);
            }

            var delay = TimeSpan.FromSeconds(Math.Min(5 * Math.Max(1, failures * failures), 300));
            try
            {
                await Task.Delay(delay, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task<string> ResolveConfigAsync(Guid workflowId, string configJson, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AutomateXDbContext>();
        var cipher = scope.ServiceProvider.GetRequiredService<SecretCipher>();

        var workspaceId = await dbContext.Workflows
            .Where(x => x.Id == workflowId)
            .Select(x => x.WorkspaceId)
            .FirstAsync(cancellationToken);

        Dictionary<string, JsonElement> connections = [];
        foreach (var connection in await dbContext.Connections
            .AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId)
            .ToListAsync(cancellationToken))
        {
            connections[connection.Name] = JsonSerializer.Deserialize<JsonElement>(cipher.Decrypt(connection.EncryptedSecrets));
        }

        // Connections are the only root that makes sense before an execution exists.
        var context = new TemplateContext(null, new Dictionary<int, JsonElement>(), Guid.Empty, workflowId, connections);
        return TemplateResolver.Resolve(configJson, context);
    }

    private async Task FireAsync(
        (Guid Id, string Type, string ConfigJson, Guid WorkflowId) row,
        string? payload,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        var dbContext = scope.ServiceProvider.GetRequiredService<AutomateXDbContext>();

        await bus.PublishAsync(new RunWorkflow(Guid.CreateVersion7(), row.WorkflowId, row.Type, payload));
        await dbContext.Triggers
            .Where(x => x.Id == row.Id)
            .ExecuteUpdateAsync(x => x.SetProperty(t => t.LastFiredAt, DateTimeOffset.UtcNow), cancellationToken);
    }
}
