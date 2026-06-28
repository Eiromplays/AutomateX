using System.Collections.Frozen;
using System.Text.Json.Nodes;
using AutomateX.Engine.Plugins;

namespace AutomateX.Engine.Triggers;

// Trigger types come from host-registered sources and GLOBAL plugins only — the same rule as event
// listeners. Plugins always run out-of-process: their trigger types are discovered by describing the
// host and recorded with the backing dll so PluginTriggerHost routes runs to the supervisor.
// Rebuild() swaps the snapshot on plugin reload; Generation lets the supervisor restart listeners.
public sealed class TriggerRegistry
{
    private readonly IReadOnlyList<ITriggerSource> _sources;
    private readonly PluginAssemblies _plugins;
    private readonly PluginProcessSupervisor _supervisor;
    private readonly ILogger<TriggerRegistry> _logger;
    private volatile FrozenDictionary<string, RegisteredTrigger> _snapshot;
    private volatile FrozenDictionary<string, string> _outOfProcDll = FrozenDictionary<string, string>.Empty;
    private int _generation;

    public TriggerRegistry(
        IEnumerable<ITriggerSource> sources,
        PluginAssemblies plugins,
        PluginProcessSupervisor supervisor,
        ILogger<TriggerRegistry> logger)
    {
        _sources = [.. sources];
        _plugins = plugins;
        _supervisor = supervisor;
        _logger = logger;
        _snapshot = Build();
    }

    // The plugin dll backing an out-of-proc trigger type, or null for host-registered types.
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
