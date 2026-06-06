using System.Reflection;
using Microsoft.Extensions.Options;

namespace AutomateX.Engine.Plugins;

public sealed record PluginAssembly(string Name, Assembly Assembly);

// Loads each plugin folder exactly once into its own collectible ALC; all discovery
// (actions, event listeners) shares these instances. Convention:
// plugins/<PluginName>/<PluginName>.dll, published with EnableDynamicLoading.
public sealed class PluginAssemblies(
    IOptions<EngineOptions> engineOptions,
    ILogger<PluginAssemblies> logger)
{
    private readonly Lock _lock = new();
    private IReadOnlyList<PluginAssembly>? _all;

    public IReadOnlyList<PluginAssembly> All
    {
        get
        {
            lock (_lock)
            {
                return _all ??= Load();
            }
        }
    }

    private List<PluginAssembly> Load()
    {
        var pluginsPath = engineOptions.Value.PluginsPath;
        if (!Path.IsPathRooted(pluginsPath))
        {
            pluginsPath = Path.Combine(AppContext.BaseDirectory, pluginsPath);
        }

        List<PluginAssembly> result = [];

        if (!Directory.Exists(pluginsPath))
        {
            logger.LogInformation("No plugins directory at {Path}, skipping plugin load", pluginsPath);
            return result;
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

            try
            {
                var loadContext = new PluginLoadContext(assemblyPath);
                result.Add(new PluginAssembly(name, loadContext.LoadFromAssemblyPath(assemblyPath)));
                logger.LogInformation("Loaded plugin assembly {Plugin}", name);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to load plugin {Plugin} from {Path}", name, assemblyPath);
            }
        }

        return result;
    }
}
