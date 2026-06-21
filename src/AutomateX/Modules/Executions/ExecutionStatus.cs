namespace AutomateX.Modules.Executions;

public enum ExecutionStatus
{
    Pending,
    Running,
    Succeeded,
    Failed,

    // Step-only: a later step that never ran because a gate closed before it.
    Skipped,

    // Step-only: a step that failed but was handled by an error edge — the run continued down
    // the error lane, so it does not fail the execution (settlement ignores Caught, like Skipped).
    Caught,
}
