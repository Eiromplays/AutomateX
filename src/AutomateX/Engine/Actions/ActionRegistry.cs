using System.Collections.Frozen;

namespace AutomateX.Engine.Actions;

// M2 turns this into the plugin seam: plugins contribute executors at load time.
public sealed class ActionRegistry(IEnumerable<IActionExecutor> executors)
{
    private readonly FrozenDictionary<string, IActionExecutor> _executors =
        executors.ToFrozenDictionary(x => x.ActionType);

    public bool Contains(string actionType) => _executors.ContainsKey(actionType);

    public IActionExecutor Get(string actionType) =>
        _executors.TryGetValue(actionType, out var executor)
            ? executor
            : throw new InvalidOperationException($"No action registered for type '{actionType}'.");
}
