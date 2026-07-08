using System.Text.Json;
using System.Text.Json.Nodes;

namespace AutomateX.Modules.Executions;

// Loop-level bookkeeping for a forEach step: the items to map, how many run at once, and how many are
// done. One row per (ExecutionId, StepOrder); removed when the loop completes. Item results live in
// ForEachItemResult (one row each) — this row only holds counters mutated by atomic UPDATEs.
public sealed class ForEachState
{
    private ForEachState()
    {
    }

    public Guid Id { get; private set; }

    public Guid ExecutionId { get; private set; }

    public int StepOrder { get; private set; }

    public Guid ChildWorkflowId { get; private set; }

    public int Total { get; private set; }

    // Max children in flight. The cap is held by launching `min(Concurrency, Total)` at enter, then one
    // more per completion — item i's completion launches item i + Concurrency (see the accumulator).
    public int Concurrency { get; private set; }

    public int CompletedCount { get; private set; }

    public string ItemsJson { get; private set; } = null!;

    public bool AnyFailed { get; private set; }

    public static ForEachState Create(
        Guid executionId, int stepOrder, Guid childWorkflowId, string itemsJson, int total, int concurrency) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            ExecutionId = executionId,
            StepOrder = stepOrder,
            ChildWorkflowId = childWorkflowId,
            Total = total,
            Concurrency = concurrency,
            CompletedCount = 0,
            ItemsJson = itemsJson,
            AnyFailed = false,
        };

    public string ItemPayload(int index) => JsonNode.Parse(ItemsJson)!.AsArray()[index]?.ToJsonString() ?? "null";

    public static bool ResultFailed(string? resultJson)
    {
        if (string.IsNullOrWhiteSpace(resultJson))
        {
            return false;
        }

        try
        {
            return JsonNode.Parse(resultJson)?["status"]?.GetValue<string>() == ExecutionStatus.Failed.ToString();
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
