using AutomateX.Plugin.Sdk;
using Microsoft.Extensions.Logging;

namespace AutomateX.SamplePlugin;

// Plugins can observe the engine, not just contribute actions. Constructor
// dependencies resolve from the host container (here: a logger).
public sealed class ExecutionCompletedListener(ILogger<ExecutionCompletedListener> logger)
    : IListenFor<ExecutionCompleted>
{
    public Task HandleAsync(ExecutionCompleted engineEvent, CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "Plugin observed execution {ExecutionId} of workflow {WorkflowId} complete",
            engineEvent.ExecutionId, engineEvent.WorkflowId);
        return Task.CompletedTask;
    }
}
