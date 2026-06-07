using AutomateX.Modules.Executions;

namespace AutomateX.Engine;

// Retry = replay: a fresh RunWorkflow carrying the original trigger payload
// byte-identical (templates see what the first run saw) on the LATEST version
// (the usual story is "fix the workflow, retry the data"). Provenance lives in
// triggeredBy — never in the payload.
public static class ExecutionRetry
{
    public static bool CanRetry(ExecutionStatus status) =>
        status is ExecutionStatus.Succeeded or ExecutionStatus.Failed;

    public static RunWorkflow Replay(Execution original) => new(
        Guid.CreateVersion7(),
        original.WorkflowId,
        $"retry:{original.Id}",
        original.TriggerPayload);

    // Lineage for read models — the original's id rides the triggeredBy marker.
    public static Guid? GetOriginalExecutionId(string triggeredBy) =>
        triggeredBy.StartsWith("retry:", StringComparison.Ordinal)
            && Guid.TryParse(triggeredBy["retry:".Length..], out var id)
                ? id
                : null;
}
