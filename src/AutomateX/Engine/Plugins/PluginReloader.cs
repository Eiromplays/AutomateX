using AutomateX.Engine.Actions;
using AutomateX.Engine.Events;
using Microsoft.Extensions.Options;

namespace AutomateX.Engine.Plugins;

public sealed record ReloadResult(int GlobalPlugins, int WorkspacePlugins);

// Reloads plugins after an install/upload. Out-of-proc (default): recycle the warm host processes so a
// replaced dll stops running, then rebuild the registries (which re-describe the hosts) and event bus.
// In-proc (legacy): swap ALCs — old ones unload lazily as executions drain — then rebuild.
public sealed class PluginReloader(
    PluginAssemblies assemblies,
    PluginProcessSupervisor supervisor,
    ActionRegistry registry,
    Triggers.TriggerRegistry triggerRegistry,
    Connections.ConnectionTypeRegistry connectionTypeRegistry,
    IOptions<EngineOptions> engineOptions,
    EngineEventBus eventBus,
    ILogger<PluginReloader> logger)
{
    private readonly bool _outOfProc = engineOptions.Value.OutOfProcPlugins;
    private readonly Lock _lock = new();

    public ReloadResult Reload()
    {
        lock (_lock)
        {
            if (_outOfProc)
            {
                supervisor.RecycleAll();
            }
            else
            {
                assemblies.Reload();
            }

            registry.Rebuild();
            triggerRegistry.Rebuild();
            connectionTypeRegistry.Rebuild();
            eventBus.Rebuild();

            var result = _outOfProc ? CountPaths() : CountAssemblies();
            logger.LogInformation(
                "Plugins reloaded: {Global} global, {Workspace} workspace-scoped",
                result.GlobalPlugins, result.WorkspacePlugins);

            return result;
        }
    }

    private ReloadResult CountPaths()
    {
        var paths = assemblies.EnumeratePaths();
        return new ReloadResult(
            paths.Count(x => x.WorkspaceId is null),
            paths.Count(x => x.WorkspaceId is not null));
    }

    private ReloadResult CountAssemblies()
    {
        var snapshot = assemblies.Current;
        return new ReloadResult(snapshot.Global.Count, snapshot.Workspaces.Sum(x => x.Value.Count));
    }
}
