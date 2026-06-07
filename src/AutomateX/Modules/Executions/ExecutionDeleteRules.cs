namespace AutomateX.Modules.Executions;

public static class ExecutionDeleteRules
{
    // In-flight executions have messages in the durable inbox that would resurrect
    // or write to ghost rows — only settled history may be removed.
    public static bool CanDelete(ExecutionStatus status) =>
        status is ExecutionStatus.Succeeded or ExecutionStatus.Failed;
}
