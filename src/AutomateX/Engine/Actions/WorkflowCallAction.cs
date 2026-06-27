using AutomateX.Plugin.Sdk;

namespace AutomateX.Engine.Actions;

public sealed record WorkflowCallConfig(Guid WorkflowId, string? Payload = null);

// Schema/discovery only — the engine intercepts `workflow.call` in ExecuteStepHandler (it starts a
// child run and suspends), so the action is never invoked. The step's output is the child's result.
public sealed record WorkflowCallResult(string Status, Guid ExecutionId);

public static class WorkflowCall
{
    public const string ActionType = "workflow.call";
}

[Action("workflow.call", "Call Workflow",
    Description = "Run another workflow and wait for it to finish. The child's result becomes this "
        + "step's output ({status, executionId, output}); branch on {{steps.<key>.output.status}}. "
        + "payload (optional) is the child's {{trigger.payload}}. Must target a workflow in this workspace.")]
public sealed class WorkflowCallAction : IAction<WorkflowCallConfig, WorkflowCallResult>
{
    public Task<WorkflowCallResult> ExecuteAsync(
        WorkflowCallConfig config, ActionContext context, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("workflow.call is handled by the engine and is never executed directly.");
}
