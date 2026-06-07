using System.Collections.Frozen;

namespace AutomateX.Engine.Actions;

// Immutable resolution table: global actions ∪ per-workspace plugin actions,
// workspace-first on type collisions. Swapped atomically on plugin reload.
public sealed class ActionSnapshot
{
    private readonly FrozenDictionary<string, RegisteredAction> _global;
    private readonly FrozenDictionary<Guid, FrozenDictionary<string, RegisteredAction>> _workspaces;

    private ActionSnapshot(
        FrozenDictionary<string, RegisteredAction> global,
        FrozenDictionary<Guid, FrozenDictionary<string, RegisteredAction>> workspaces)
    {
        _global = global;
        _workspaces = workspaces;
    }

    public static ActionSnapshot Compose(
        IEnumerable<RegisteredAction> globalActions,
        IReadOnlyDictionary<Guid, IReadOnlyList<RegisteredAction>> workspaceActions)
    {
        Dictionary<string, RegisteredAction> global = [];
        foreach (var action in globalActions)
        {
            global[action.Descriptor.Type] = action; // later registrations win
        }

        var workspaces = workspaceActions.ToFrozenDictionary(
            x => x.Key,
            x =>
            {
                Dictionary<string, RegisteredAction> actions = [];
                foreach (var action in x.Value)
                {
                    actions[action.Descriptor.Type] = action;
                }

                return actions.ToFrozenDictionary();
            });

        return new ActionSnapshot(global.ToFrozenDictionary(), workspaces);
    }

    public bool Contains(string actionType, Guid workspaceId) =>
        (_workspaces.TryGetValue(workspaceId, out var workspace) && workspace.ContainsKey(actionType))
        || _global.ContainsKey(actionType);

    public IActionExecutor Get(string actionType, Guid workspaceId)
    {
        if (_workspaces.TryGetValue(workspaceId, out var workspace)
            && workspace.TryGetValue(actionType, out var scoped))
        {
            return scoped.Executor;
        }

        return _global.TryGetValue(actionType, out var action)
            ? action.Executor
            : throw new InvalidOperationException($"No action registered for type '{actionType}'.");
    }

    // All action types contributed by one source (e.g. "plugin:Name" globally, or
    // "workspace:Name" within the given workspace) — used by the delete guard.
    public IReadOnlyList<string> ActionTypesFromSource(string source, Guid? workspaceId)
    {
        var pool = workspaceId is { } ws
            ? _workspaces.TryGetValue(ws, out var scoped) ? scoped.Values : []
            : _global.Values;

        return pool
            .Where(x => string.Equals(x.Descriptor.Source, source, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Descriptor.Type)
            .ToList();
    }

    public IReadOnlyList<ActionDescriptor> Descriptors(Guid workspaceId)
    {
        var merged = _global.ToDictionary(x => x.Key, x => x.Value.Descriptor);
        if (_workspaces.TryGetValue(workspaceId, out var workspace))
        {
            foreach (var (type, action) in workspace)
            {
                merged[type] = action.Descriptor;
            }
        }

        return [.. merged.Values];
    }
}
