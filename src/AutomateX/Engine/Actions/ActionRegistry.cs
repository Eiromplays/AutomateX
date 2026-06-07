using AutomateX.Engine.Plugins;

namespace AutomateX.Engine.Actions;

// Resolves actions per workspace: host executors + built-ins + global plugins are
// available everywhere; workspace plugins only in their workspace (shadowing global
// on collisions). Rebuild() swaps the snapshot atomically after a plugin reload —
// in-flight executions keep the executor they already resolved.
public sealed class ActionRegistry
{
    private readonly IReadOnlyList<IActionExecutor> _hostExecutors;
    private readonly IReadOnlyList<IActionSource> _sources;
    private readonly PluginAssemblies _plugins;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ActionRegistry> _logger;
    private volatile ActionSnapshot _snapshot;

    public ActionRegistry(
        IEnumerable<IActionExecutor> executors,
        IEnumerable<IActionSource> sources,
        PluginAssemblies plugins,
        IServiceProvider serviceProvider,
        ILogger<ActionRegistry> logger)
    {
        _hostExecutors = [.. executors];
        _sources = [.. sources];
        _plugins = plugins;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _snapshot = Build();
    }

    public void Rebuild() => _snapshot = Build();

    public bool Contains(string actionType, Guid workspaceId) => _snapshot.Contains(actionType, workspaceId);

    public IActionExecutor Get(string actionType, Guid workspaceId) => _snapshot.Get(actionType, workspaceId);

    public IReadOnlyList<ActionDescriptor> Descriptors(Guid workspaceId) => _snapshot.Descriptors(workspaceId);

    private ActionSnapshot Build()
    {
        List<RegisteredAction> global = [];

        foreach (var executor in _hostExecutors)
        {
            global.Add(new RegisteredAction(
                new ActionDescriptor(executor.ActionType, executor.ActionType, null, "host", null, null),
                executor));
        }

        foreach (var source in _sources)
        {
            global.AddRange(source.GetActions());
        }

        var plugins = _plugins.Current;
        foreach (var plugin in plugins.Global)
        {
            global.AddRange(Discover(plugin, $"plugin:{plugin.Name}"));
        }

        var workspaces = plugins.Workspaces.ToDictionary(
            x => x.Key,
            x => (IReadOnlyList<RegisteredAction>)x.Value
                .SelectMany(plugin => Discover(plugin, $"workspace:{plugin.Name}"))
                .ToList());

        return ActionSnapshot.Compose(global, workspaces);
    }

    private List<RegisteredAction> Discover(PluginAssembly plugin, string source)
    {
        try
        {
            var actions = ActionDiscovery.FromAssembly(plugin.Assembly, source, _serviceProvider).ToList();
            foreach (var action in actions)
            {
                _logger.LogInformation("Registered action {ActionType} from {Source}", action.Descriptor.Type, source);
            }

            return actions;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to discover actions in plugin {Plugin}", plugin.Name);
            return [];
        }
    }
}
