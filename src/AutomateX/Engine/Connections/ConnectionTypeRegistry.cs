using System.Collections.Frozen;
using AutomateX.Engine.Plugins;
using AutomateX.Plugin.Sdk;

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
    private volatile FrozenDictionary<string, ConnectionTypeDescriptor> _descriptors;
    private volatile FrozenDictionary<string, IConnectionType> _instances;

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
        (_descriptors, _instances) = Build();
    }

    public void Rebuild() => (_descriptors, _instances) = Build();

    public IReadOnlyCollection<ConnectionTypeDescriptor> Descriptors => _descriptors.Values;

    // The live type instance for a provider key — used to run its credential test.
    public IConnectionType? GetInstance(string typeKey) => _instances.GetValueOrDefault(typeKey);

    private (FrozenDictionary<string, ConnectionTypeDescriptor> Descriptors, FrozenDictionary<string, IConnectionType> Instances) Build()
    {
        Dictionary<string, ConnectionTypeDescriptor> descriptors = [];
        Dictionary<string, IConnectionType> instances = [];

        foreach (var registered in _sources.SelectMany(x => x.GetConnectionTypes()))
        {
            Add(registered);
        }

        foreach (var plugin in _plugins.Current.Global)
        {
            try
            {
                foreach (var registered in ConnectionTypeDiscovery.FromAssembly(plugin.Assembly, $"plugin:{plugin.Name}", _serviceProvider))
                {
                    Add(registered);
                    _logger.LogInformation("Registered connection type {Type} from plugin {Plugin}", registered.Descriptor.Type, plugin.Name);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to discover connection types in plugin {Plugin}", plugin.Name);
            }
        }

        return (descriptors.ToFrozenDictionary(), instances.ToFrozenDictionary());

        void Add(RegisteredConnectionType registered)
        {
            descriptors[registered.Descriptor.Type] = registered.Descriptor;
            instances[registered.Descriptor.Type] = registered.Instance;
        }
    }
}

public sealed class BuiltInConnectionTypeSource(IServiceProvider serviceProvider) : IConnectionTypeSource
{
    public IEnumerable<RegisteredConnectionType> GetConnectionTypes() =>
        ConnectionTypeDiscovery.FromAssembly(typeof(BuiltInConnectionTypeSource).Assembly, "builtin", serviceProvider);
}
