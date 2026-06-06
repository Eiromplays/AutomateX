using System.Collections.Frozen;

namespace AutomateX.Engine.Actions;

// Built once at startup from raw executors (tests, host-internal) and action sources
// (built-in assembly scan + loaded plugins). Later registrations win on type collisions.
public sealed class ActionRegistry
{
    private readonly FrozenDictionary<string, RegisteredAction> _actions;

    public ActionRegistry(IEnumerable<IActionExecutor> executors, IEnumerable<IActionSource> sources)
    {
        Dictionary<string, RegisteredAction> actions = [];

        foreach (var executor in executors)
        {
            actions[executor.ActionType] = new RegisteredAction(
                new ActionDescriptor(executor.ActionType, executor.ActionType, null, "host", null, null),
                executor);
        }

        foreach (var action in sources.SelectMany(x => x.GetActions()))
        {
            actions[action.Descriptor.Type] = action;
        }

        _actions = actions.ToFrozenDictionary();
        Descriptors = _actions.Values.Select(x => x.Descriptor).ToList();
    }

    public IReadOnlyCollection<ActionDescriptor> Descriptors { get; }

    public bool Contains(string actionType) => _actions.ContainsKey(actionType);

    public IActionExecutor Get(string actionType) =>
        _actions.TryGetValue(actionType, out var action)
            ? action.Executor
            : throw new InvalidOperationException($"No action registered for type '{actionType}'.");
}
