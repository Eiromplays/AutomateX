namespace AutomateX.Modules.Executions;

public enum ExecutionStatus
{
    Pending,
    Running,
    Succeeded,
    Failed,

    // Paused at a wait step until resumed by a timer or an external signal (durable wait/approval).
    // Not running, but not terminal — the sweeper leaves it alone.
    Waiting,

    // Step-only: a later step that never ran because a gate closed before it.
    Skipped,

    // Step-only: a step that failed but was handled by an error edge — the run continued down
    // the error lane, so it does not fail the execution (settlement ignores Caught, like Skipped).
    Caught,
}
