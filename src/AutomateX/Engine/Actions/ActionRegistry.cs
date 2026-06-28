using System.Text.Json.Nodes;
using AutomateX.Engine.Plugins;

namespace AutomateX.Engine.Actions;

// Resolves actions per workspace: host executors + built-ins + global plugins are available everywhere;
// workspace plugins only in their workspace (shadowing global on collisions). Plugins always run
// out-of-process — discovered by describing each host, never loaded in-host. Rebuild() swaps the
// snapshot atomically after a plugin reload; in-flight executions keep the executor they resolved.
public sealed class ActionRegistry
{
    private readonly IReadOnlyList<IActionExecutor> _hostExecutors;
    private readonly IReadOnlyList<IActionSource> _sources;
    private readonly PluginAssemblies _plugins;
    private readonly PluginProcessSupervisor _supervisor;
    private readonly ILogger<ActionRegistry> _logger;
    private volatile ActionSnapshot _snapshot;

    public ActionRegistry(
        IEnumerable<IActionExecutor> executors,
        IEnumerable<IActionSource> sources,
        PluginAssemblies plugins,
        PluginProcessSupervisor supervisor,
        ILogger<ActionRegistry> logger)
    {
        _hostExecutors = [.. executors];
        _sources = [.. sources];
        _plugins = plugins;
        _supervisor = supervisor;
        _logger = logger;
        _snapshot = Build();
    }

    public void Rebuild() => _snapshot = Build();

    public bool Contains(string actionType, Guid workspaceId) => _snapshot.Contains(actionType, workspaceId);

    public IActionExecutor Get(string actionType, Guid workspaceId) => _snapshot.Get(actionType, workspaceId);

    public IReadOnlyList<ActionDescriptor> Descriptors(Guid workspaceId) => _snapshot.Descriptors(workspaceId);

    public IReadOnlyList<string> ActionTypesFromSource(string source, Guid? workspaceId) =>
        _snapshot.ActionTypesFromSource(source, workspaceId);

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

        // Plugins run out-of-process: discover by describing each host, never loading it in-host.
        var paths = _plugins.EnumeratePaths();
        foreach (var path in paths.Where(p => p.WorkspaceId is null))
        {
            global.AddRange(DescribeOutOfProc(path, $"plugin:{path.Name}"));
        }

        var workspaces = paths
            .Where(p => p.WorkspaceId is not null)
            .GroupBy(p => p.WorkspaceId!.Value)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<RegisteredAction>)g.SelectMany(p => DescribeOutOfProc(p, $"workspace:{p.Name}")).ToList());

        return ActionSnapshot.Compose(global, workspaces);
    }

    // Describe a plugin host (blocking at startup/reload — not a request path) and map its actions to
    // proc-call executors.
    private List<RegisteredAction> DescribeOutOfProc(PluginPath path, string source)
    {
        try
        {
            var described = _supervisor.DescribeAsync(path.DllPath).GetAwaiter().GetResult();
            List<RegisteredAction> result = [];
            foreach (var action in (JsonArray?)described["result"]?["actions"] ?? [])
            {
                var type = (string)action!["type"]!;
                result.Add(new RegisteredAction(
                    new ActionDescriptor(
                        type,
                        (string?)action["displayName"] ?? type,
                        (string?)action["description"],
                        source,
                        action["configSchema"]?.DeepClone(),
                        action["resultSchema"]?.DeepClone()),
                    new ProcCallActionExecutor(_supervisor, path.DllPath, type)));
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to describe out-of-proc plugin {Plugin}", path.Name);
            return [];
        }
    }
}
