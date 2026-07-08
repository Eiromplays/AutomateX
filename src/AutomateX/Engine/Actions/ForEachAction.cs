using System.Text.Json;
using AutomateX.Plugin.Sdk;

namespace AutomateX.Engine.Actions;

// Concurrency = max children in flight (default 1 = sequential); clamped to [1, min(total, ceiling)]
// at enter, where the ceiling is Engine:ForEachMaxConcurrency.
public sealed record ForEachConfig(JsonElement Items, Guid WorkflowId, int Concurrency = 1);

public static class ForEach
{
    public const string ActionType = "forEach";
}

// Schema/discovery only — the engine intercepts `forEach` in ExecuteStepHandler (it maps a child
// workflow over the items and accumulates the results). The step output is the ordered results array.
[Action("forEach", "For Each",
    Description = "Run a workflow once per item of an array and collect the results in order. items is "
        + "an array (e.g. {{steps.fetch.output.rows}}); each item becomes the child's {{trigger.payload}}. "
        + "The step output is the array of child results ({status, output} each). Set concurrency to run "
        + "several items at once (default 1 = sequential).")]
public sealed class ForEachAction : IAction<ForEachConfig, JsonElement>
{
    public Task<JsonElement> ExecuteAsync(
        ForEachConfig config, ActionContext context, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("forEach is handled by the engine and is never executed directly.");
}
