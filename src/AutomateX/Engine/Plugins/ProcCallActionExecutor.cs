using AutomateX.Engine.Actions;

namespace AutomateX.Engine.Plugins;

// An action that lives in an out-of-process plugin: execution is marshalled to its host process via
// the supervisor instead of an in-host IAction instance.
internal sealed class ProcCallActionExecutor(PluginProcessSupervisor supervisor, string pluginDll, string actionType)
    : IActionExecutor
{
    public string ActionType => actionType;

    public Task<string?> ExecuteAsync(string configJson, ActionInvocation invocation, CancellationToken cancellationToken = default) =>
        supervisor.ExecuteActionAsync(pluginDll, actionType, configJson, cancellationToken);
}
