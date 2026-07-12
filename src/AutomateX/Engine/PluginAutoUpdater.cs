using AutomateX.Database;
using AutomateX.Engine.Plugins;
using AutomateX.Modules.Plugins;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AutomateX.Engine;

// Opt-in automatic plugin updates (v5.2). A cluster-singleton (SingularBackgroundAgent) so N nodes don't
// each update the same plugin. For every globally-installed plugin flagged AutoUpdate it moves to the
// highest compatible, same-major version from a VERIFIED source — never untrusted, never a major bump —
// reusing the same install path (verify -> extract -> reload -> provenance) as a manual install. Idle
// unless Engine:PluginAutoUpdateInterval is set and plugin management is enabled.
public sealed class PluginAutoUpdater(
    IServiceProvider serviceProvider,
    IHttpClientFactory httpClientFactory,
    IOptions<EngineOptions> engineOptions,
    ILogger<PluginAutoUpdater> logger) : SingularBackgroundAgent("automatex-plugin-autoupdate")
{
    protected override async Task RunAsync(CancellationToken stoppingToken)
    {
        if (engineOptions.Value.PluginAutoUpdateInterval is not { } interval || !engineOptions.Value.AllowPluginUpload)
        {
            return;
        }

        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await CheckAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Plugin auto-update tick failed");
            }
        }
    }

    private async Task CheckAsync(CancellationToken ct)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AutomateXDbContext>();
        var installer = scope.ServiceProvider.GetRequiredService<PluginInstaller>();

        var candidates = await dbContext.InstalledPlugins
            .Where(x => x.WorkspaceId == Guid.Empty && x.AutoUpdate)
            .Select(x => new { x.Name, x.PluginId, x.Version })
            .ToListAsync(ct);
        if (candidates.Count == 0)
        {
            return;
        }

        using var http = httpClientFactory.CreateClient();
        var sources = await PluginCatalogFetch.ReadAllAsync(http, engineOptions.Value, dbContext, ct);

        foreach (var candidate in candidates)
        {
            // Only from a Verified source, and never a major bump (AutoUpdate.Choose enforces the rest).
            if (PluginCatalogResolve.Best(sources, candidate.PluginId, candidate.Name) is not { } resolved
                || resolved.Source.Result.Trust != CatalogTrust.Verified)
            {
                continue;
            }

            if (AutoUpdate.Choose(candidate.Version, resolved.Plugin.Versions, AppVersion.Current) is not { } target)
            {
                continue;
            }

            try
            {
                var content = await http.GetByteArrayAsync(target.Url, ct);
                await installer.InstallAsync(
                    content, resolved.Plugin.Manifest, target, resolved.Source.SourceId,
                    resolved.Source.Result.Index?.Publisher?.Name, "auto-update", ct);
                logger.LogInformation(
                    "Auto-updated {Plugin} {From} to {To}", candidate.Name, candidate.Version, target.Version);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Auto-update of {Plugin} failed", candidate.Name);
            }
        }
    }
}
