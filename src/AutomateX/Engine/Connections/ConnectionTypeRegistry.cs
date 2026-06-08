using System.Collections.Frozen;
using AutomateX.Engine.Plugins;

namespace AutomateX.Engine.Connections;

// Connection types come from host sources + GLOBAL plugins (same rule as triggers/
// event listeners — workspace plugins contribute actions only). Rebuild() swaps the
// snapshot on plugin reload, so installing a plugin teaches the UI its connection shape.
public sealed class ConnectionTypeRegistry
{
    private readonly IReadOnlyList<IConnectionTypeSource> _sources;
    private readonly PluginAssemblies _plugins;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ConnectionTypeRegistry> _logger;
    private volatile FrozenDictionary<string, ConnectionTypeDescriptor> _snapshot;

    public ConnectionTypeRegistry(
        IEnumerable<IConnectionTypeSource> sources,
        PluginAssemblies plugins,
        IServiceProvider serviceProvider,
        ILogger<ConnectionTypeRegistry> logger)
    {
        _sources = [.. sources];
        _plugins = plugins;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _snapshot = Build();
    }

    public void Rebuild() => _snapshot = Build();

    public IReadOnlyCollection<ConnectionTypeDescriptor> Descriptors => _snapshot.Values;

    private FrozenDictionary<string, ConnectionTypeDescriptor> Build()
    {
        Dictionary<string, ConnectionTypeDescriptor> types = [];

        foreach (var descriptor in _sources.SelectMany(x => x.GetConnectionTypes()))
        {
            types[descriptor.Type] = descriptor;
        }

        foreach (var plugin in _plugins.Current.Global)
        {
            try
            {
                foreach (var descriptor in ConnectionTypeDiscovery.FromAssembly(plugin.Assembly, $"plugin:{plugin.Name}", _serviceProvider))
                {
                    types[descriptor.Type] = descriptor;
                    _logger.LogInformation("Registered connection type {Type} from plugin {Plugin}", descriptor.Type, plugin.Name);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to discover connection types in plugin {Plugin}", plugin.Name);
            }
        }

        return types.ToFrozenDictionary();
    }
}

public sealed class BuiltInConnectionTypeSource(IServiceProvider serviceProvider) : IConnectionTypeSource
{
    public IEnumerable<ConnectionTypeDescriptor> GetConnectionTypes() =>
        ConnectionTypeDiscovery.FromAssembly(typeof(BuiltInConnectionTypeSource).Assembly, "builtin", serviceProvider);
}
