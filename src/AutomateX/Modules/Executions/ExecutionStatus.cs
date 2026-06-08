namespace AutomateX.Modules.Executions;

public enum ExecutionStatus
{
    Pending,
    Running,
    Succeeded,
    Failed,

    // Step-only: a later step that never ran because a gate closed before it.
    Skipped,
}
