using Microsoft.Extensions.Options;

namespace AutomateX.Engine.Plugins;

// Dev convenience, off by default: watches the plugins directory and hot-reloads
// after writes settle (debounce — `dotnet publish` drops many files). Safe against
// self-triggering because reloads shadow-copy to temp and never write back here.
public sealed class PluginWatcher(
    PluginReloader reloader,
    PluginAssemblies plugins,
    IOptions<EngineOptions> engineOptions,
    ILogger<PluginWatcher> logger) : BackgroundService
{
    private static readonly TimeSpan QuietPeriod = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!engineOptions.Value.WatchPlugins)
        {
            return;
        }

        var root = plugins.GlobalRoot;
        Directory.CreateDirectory(root);

        long lastChangeTicks = 0;
        long lastReloadTicks = DateTimeOffset.UtcNow.Ticks;

        using var watcher = new FileSystemWatcher(root)
        {
            IncludeSubdirectories = true,
            EnableRaisingEvents = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.DirectoryName,
        };

        void Touch(object sender, FileSystemEventArgs e) =>
            Interlocked.Exchange(ref lastChangeTicks, DateTimeOffset.UtcNow.Ticks);

        watcher.Created += Touch;
        watcher.Changed += Touch;
        watcher.Deleted += Touch;
        watcher.Renamed += (s, e) => Touch(s, e);

        logger.LogInformation("Watching {Path} for plugin changes", root);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500), stoppingToken);

            var changed = Interlocked.Read(ref lastChangeTicks);
            if (changed == 0 || changed <= Interlocked.Read(ref lastReloadTicks))
            {
                continue;
            }

            if (DateTimeOffset.UtcNow.Ticks - changed < QuietPeriod.Ticks)
            {
                continue; // still settling
            }

            Interlocked.Exchange(ref lastReloadTicks, DateTimeOffset.UtcNow.Ticks);
            try
            {
                var result = reloader.Reload();
                logger.LogInformation(
                    "Plugins dir changed — hot-reloaded ({Global} global, {Workspace} workspace-scoped)",
                    result.GlobalPlugins, result.WorkspacePlugins);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Watch-triggered plugin reload failed");
            }
        }
    }
}
