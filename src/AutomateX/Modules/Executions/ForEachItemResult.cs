namespace AutomateX.Modules.Executions;

// One row per completed forEach item: each child writes only its own slot, so parallel completions never
// contend and idempotency comes from the unique (ExecutionId, StepOrder, ItemIndex) key. The finisher
// gathers these ordered by ItemIndex to build the loop's result array, then deletes them.
public sealed class ForEachItemResult
{
    private ForEachItemResult()
    {
    }

    public Guid Id { get; private set; }

    public Guid ExecutionId { get; private set; }

    public int StepOrder { get; private set; }

    public int ItemIndex { get; private set; }

    public string ResultJson { get; private set; } = null!;

    public bool Failed { get; private set; }
}
