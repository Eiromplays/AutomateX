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

    // Lineage for read models — the original's id rides the triggeredBy marker
    // ("retry:<id>" for a full replay, "retry-from:<id>" for a retry from a step).
    public static Guid? GetOriginalExecutionId(string triggeredBy)
    {
        foreach (var prefix in (string[])["retry:", "retry-from:"])
        {
            if (triggeredBy.StartsWith(prefix, StringComparison.Ordinal)
                && Guid.TryParse(triggeredBy[prefix.Length..], out var id))
            {
                return id;
            }
        }

        return null;
    }
}
