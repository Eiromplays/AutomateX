using System.Collections.Frozen;
using AutomateX.Engine.Plugins;

namespace AutomateX.Engine.Triggers;

// Trigger types come from host-registered sources and GLOBAL plugins only —
// the same rule as event listeners: workspace plugins contribute actions,
// never instance-wide machinery. Rebuild() swaps the snapshot on plugin reload;
// Generation lets the supervisor restart listeners onto new code.
public sealed class TriggerRegistry
{
    private readonly IReadOnlyList<ITriggerSource> _sources;
    private readonly PluginAssemblies _plugins;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TriggerRegistry> _logger;
    private volatile FrozenDictionary<string, RegisteredTrigger> _snapshot;
    private int _generation;

    public TriggerRegistry(
        IEnumerable<ITriggerSource> sources,
        PluginAssemblies plugins,
        IServiceProvider serviceProvider,
        ILogger<TriggerRegistry> logger)
    {
        _sources = [.. sources];
        _plugins = plugins;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _snapshot = Build();
    }

    public int Generation => _generation;

    public void Rebuild()
    {
        _snapshot = Build();
        Interlocked.Increment(ref _generation);
    }

    public bool Contains(string triggerType) => _snapshot.ContainsKey(triggerType);

    public IReadOnlyCollection<string> Types => _snapshot.Keys;

    public IReadOnlyCollection<TriggerDescriptor> Descriptors =>
        _snapshot.Values.Select(x => x.Descriptor).ToList();

    public ITriggerRunner? CreateRunner(string triggerType) =>
        _snapshot.TryGetValue(triggerType, out var trigger) ? trigger.CreateRunner() : null;

    private FrozenDictionary<string, RegisteredTrigger> Build()
    {
        Dictionary<string, RegisteredTrigger> triggers = [];

        foreach (var trigger in _sources.SelectMany(x => x.GetTriggers()))
        {
            triggers[trigger.Descriptor.Type] = trigger;
        }

        foreach (var plugin in _plugins.Current.Global)
        {
            try
            {
                foreach (var trigger in TriggerDiscovery.FromAssembly(plugin.Assembly, $"plugin:{plugin.Name}", _serviceProvider))
                {
                    triggers[trigger.Descriptor.Type] = trigger;
                    _logger.LogInformation(
                        "Registered trigger type {TriggerType} from plugin {Plugin}", trigger.Descriptor.Type, plugin.Name);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to discover triggers in plugin {Plugin}", plugin.Name);
            }
        }

        return triggers.ToFrozenDictionary();
    }
}
