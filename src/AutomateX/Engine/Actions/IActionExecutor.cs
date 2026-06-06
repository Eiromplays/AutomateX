namespace AutomateX.Engine.Actions;

public sealed record ActionInvocation(Guid ExecutionId, Guid WorkflowId, int StepOrder);

// Engine-side dispatch contract: string-typed, json in/json out. Configs arrive
// already template-resolved. SDK actions are adapted onto this via SdkActionExecutor.
public interface IActionExecutor
{
    string ActionType { get; }

    Task<string?> ExecuteAsync(string configJson, ActionInvocation invocation, CancellationToken cancellationToken = default);
}
