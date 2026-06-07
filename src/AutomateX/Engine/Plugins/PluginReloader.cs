using AutomateX.Engine.Actions;
using AutomateX.Engine.Events;

namespace AutomateX.Engine.Plugins;

public sealed record ReloadResult(int GlobalPlugins, int WorkspacePlugins);

// The three swaps, in dependency order: assemblies (new ALCs, old ones unloading
// lazily as executions drain) → action registry → event bus subscriptions.
public sealed class PluginReloader(
    PluginAssemblies assemblies,
    ActionRegistry registry,
    Triggers.TriggerRegistry triggerRegistry,
    EngineEventBus eventBus,
    ILogger<PluginReloader> logger)
{
    private readonly Lock _lock = new();

    public ReloadResult Reload()
    {
        lock (_lock)
        {
            assemblies.Reload();
            registry.Rebuild();
            triggerRegistry.Rebuild();
            eventBus.Rebuild();

            var snapshot = assemblies.Current;
            var result = new ReloadResult(
                snapshot.Global.Count,
                snapshot.Workspaces.Sum(x => x.Value.Count));

            logger.LogInformation(
                "Plugins reloaded: {Global} global, {Workspace} workspace-scoped",
                result.GlobalPlugins, result.WorkspacePlugins);

            return result;
        }
    }
}
