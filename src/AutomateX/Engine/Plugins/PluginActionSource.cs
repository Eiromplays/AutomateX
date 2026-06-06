using AutomateX.Engine.Actions;
using Microsoft.Extensions.Options;

namespace AutomateX.Engine.Plugins;

// Convention: plugins/<PluginName>/<PluginName>.dll, one folder per plugin,
// published with EnableDynamicLoading so dependencies sit next to the assembly.
public sealed class PluginActionSource(
    IServiceProvider serviceProvider,
    IOptions<EngineOptions> engineOptions,
    ILogger<PluginActionSource> logger) : IActionSource
{
    public IEnumerable<RegisteredAction> GetActions()
    {
        var pluginsPath = engineOptions.Value.PluginsPath;
        if (!Path.IsPathRooted(pluginsPath))
        {
            pluginsPath = Path.Combine(AppContext.BaseDirectory, pluginsPath);
        }

        if (!Directory.Exists(pluginsPath))
        {
            logger.LogInformation("No plugins directory at {Path}, skipping plugin load", pluginsPath);
            yield break;
        }

        foreach (var directory in Directory.EnumerateDirectories(pluginsPath))
        {
            var name = Path.GetFileName(directory);
            var assemblyPath = Path.Combine(directory, $"{name}.dll");
            if (!File.Exists(assemblyPath))
            {
                logger.LogWarning("Plugin folder {Plugin} contains no {Assembly}, skipping", name, $"{name}.dll");
                continue;
            }

            List<RegisteredAction> actions = [];
            try
            {
                var loadContext = new PluginLoadContext(assemblyPath);
                var assembly = loadContext.LoadFromAssemblyPath(assemblyPath);
                actions.AddRange(ActionDiscovery.FromAssembly(assembly, $"plugin:{name}", serviceProvider));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to load plugin {Plugin} from {Path}", name, assemblyPath);
                continue;
            }

            foreach (var action in actions)
            {
                logger.LogInformation("Loaded action {ActionType} from plugin {Plugin}", action.Descriptor.Type, name);
                yield return action;
            }
        }
    }
}
