using AutomateX.Engine.Actions;

namespace AutomateX.Engine.Plugins;

public sealed class PluginActionSource(
    PluginAssemblies pluginAssemblies,
    IServiceProvider serviceProvider,
    ILogger<PluginActionSource> logger) : IActionSource
{
    public IEnumerable<RegisteredAction> GetActions()
    {
        foreach (var plugin in pluginAssemblies.All)
        {
            List<RegisteredAction> actions = [];
            try
            {
                actions.AddRange(ActionDiscovery.FromAssembly(plugin.Assembly, $"plugin:{plugin.Name}", serviceProvider));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to discover actions in plugin {Plugin}", plugin.Name);
                continue;
            }

            foreach (var action in actions)
            {
                logger.LogInformation("Loaded action {ActionType} from plugin {Plugin}", action.Descriptor.Type, plugin.Name);
                yield return action;
            }
        }
    }
}
