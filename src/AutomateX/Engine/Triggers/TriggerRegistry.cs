using System.Collections.Frozen;
using System.Text.Json.Nodes;
using AutomateX.Engine.Plugins;
using Microsoft.Extensions.Options;

namespace AutomateX.Engine.Triggers;

// Trigger types come from host-registered sources and GLOBAL plugins only —
// the same rule as event listeners: workspace plugins contribute actions,
// never instance-wide machinery. Rebuild() swaps the snapshot on plugin reload;
// Generation lets the supervisor restart listeners onto new code.
public sealed class TriggerRegistry
{
    private readonly IReadOnlyList<ITriggerSource> _sources;
    private readonly PluginAssemblies _plugins;
    private readonly PluginProcessSupervisor _supervisor;
    private readonly bool _outOfProc;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TriggerRegistry> _logger;
    private volatile FrozenDictionary<string, RegisteredTrigger> _snapshot;
    private volatile FrozenDictionary<string, string> _outOfProcDll = FrozenDictionary<string, string>.Empty;
    private int _generation;

    public TriggerRegistry(
        IEnumerable<ITriggerSource> sources,
        PluginAssemblies plugins,
        PluginProcessSupervisor supervisor,
        IOptions<EngineOptions> engineOptions,
        IServiceProvider serviceProvider,
        ILogger<TriggerRegistry> logger)
    {
        _sources = [.. sources];
        _plugins = plugins;
        _supervisor = supervisor;
        _outOfProc = engineOptions.Value.OutOfProcPlugins;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _snapshot = Build();
    }

    // The plugin dll backing an out-of-proc trigger type, or null for in-proc/host types.
    public string? OutOfProcPluginDll(string triggerType) => _outOfProcDll.GetValueOrDefault(triggerType);

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

        if (_outOfProc)
        {
            Dictionary<string, string> outOfProc = [];
            foreach (var path in _plugins.EnumeratePaths().Where(p => p.WorkspaceId is null))
            {
                foreach (var trigger in DescribeOutOfProc(path))
                {
                    triggers[trigger.Descriptor.Type] = trigger;
                    outOfProc[trigger.Descriptor.Type] = path.DllPath;
                }
            }

            _outOfProcDll = outOfProc.ToFrozenDictionary();
        }
        else
        {
            _outOfProcDll = FrozenDictionary<string, string>.Empty;
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
        }

        return triggers.ToFrozenDictionary();
    }

    // Describe a plugin host's triggers (blocking at startup/reload). CreateRunner is never used for
    // these — the host routes them to the supervisor — so it throws if called.
    private IEnumerable<RegisteredTrigger> DescribeOutOfProc(PluginPath path)
    {
        JsonArray triggers;
        try
        {
            var described = _supervisor.DescribeAsync(path.DllPath).GetAwaiter().GetResult();
            triggers = (JsonArray?)described["result"]?["triggers"] ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to describe out-of-proc plugin {Plugin}", path.Name);
            yield break;
        }

        foreach (var trigger in triggers)
        {
            var type = (string)trigger!["type"]!;
            yield return new RegisteredTrigger(
                new TriggerDescriptor(
                    type,
                    (string?)trigger["displayName"] ?? type,
                    (string?)trigger["description"],
                    $"plugin:{path.Name}",
                    trigger["configSchema"]?.DeepClone()),
                () => throw new InvalidOperationException($"Out-of-proc trigger '{type}' runs via the supervisor."));
        }
    }
}
