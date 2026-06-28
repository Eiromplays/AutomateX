using AutomateX.Engine.Actions;
using AutomateX.Engine.Events;

namespace AutomateX.Engine.Plugins;

public sealed record ReloadResult(int GlobalPlugins, int WorkspacePlugins);

// Reloads plugins after an install/upload: recycle the warm host processes so a replaced dll stops
// running, then rebuild the registries (which re-describe the hosts) and the event bus.
public sealed class PluginReloader(
    PluginAssemblies assemblies,
    PluginProcessSupervisor supervisor,
    ActionRegistry registry,
    Triggers.TriggerRegistry triggerRegistry,
    Connections.ConnectionTypeRegistry connectionTypeRegistry,
    EngineEventBus eventBus,
    ILogger<PluginReloader> logger)
{
    private readonly Lock _lock = new();

    public ReloadResult Reload()
    {
        lock (_lock)
        {
            supervisor.RecycleAll();
            registry.Rebuild();
            triggerRegistry.Rebuild();
            connectionTypeRegistry.Rebuild();
            eventBus.Rebuild();

            var paths = assemblies.EnumeratePaths();
            var result = new ReloadResult(
                paths.Count(x => x.WorkspaceId is null),
                paths.Count(x => x.WorkspaceId is not null));

            logger.LogInformation(
                "Plugins reloaded: {Global} global, {Workspace} workspace-scoped",
                result.GlobalPlugins, result.WorkspacePlugins);

            return result;
        }
    }
}
