using Microsoft.Extensions.Options;

namespace AutomateX.Engine.Plugins;

// Samples plugin host memory on an interval and recycles any host over the configured cap. A pure
// no-op unless Engine__PluginLimits__MaxMemoryBytes is set.
public sealed class PluginMemoryWatchdog(
    PluginProcessSupervisor supervisor,
    IOptions<EngineOptions> options,
    ILogger<PluginMemoryWatchdog> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var limits = options.Value.PluginLimits;
        if (limits.MaxMemoryBytes is null)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                supervisor.EnforceMemoryLimits();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Plugin memory watchdog sweep failed");
            }

            try
            {
                await Task.Delay(limits.MemoryCheckInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
