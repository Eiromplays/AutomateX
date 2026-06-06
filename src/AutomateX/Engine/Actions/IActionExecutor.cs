namespace AutomateX.Engine.Actions;

// Engine-side dispatch contract: string-typed, json in/json out.
// SDK actions are adapted onto this via SdkActionExecutor.
public interface IActionExecutor
{
    string ActionType { get; }

    Task<string?> ExecuteAsync(string configJson, CancellationToken cancellationToken = default);
}
