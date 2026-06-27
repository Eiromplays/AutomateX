using System.Text.Json;
using System.Text.Json.Nodes;

namespace AutomateX.Modules.Executions;

// Durable accumulator for a forEach step: the items to map, how far we've launched, and each item's
// result slot. One row per (ExecutionId, StepOrder); cleared when the loop completes.
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

    // Next item index to launch (sequential v1 launches one at a time).
    public int NextIndex { get; private set; }

    public int CompletedCount { get; private set; }

    public string ItemsJson { get; private set; } = null!;

    // A JSON array with one slot per item; slot i holds item i's result (null until done).
    public string ResultsJson { get; private set; } = null!;

    public bool AnyFailed { get; private set; }

    public bool IsComplete => CompletedCount >= Total;

    public static ForEachState Create(Guid executionId, int stepOrder, Guid childWorkflowId, string itemsJson, int total)
    {
        var slots = new JsonArray();
        for (var i = 0; i < total; i++)
        {
            slots.Add((JsonNode?)null);
        }

        return new ForEachState
        {
            Id = Guid.CreateVersion7(),
            ExecutionId = executionId,
            StepOrder = stepOrder,
            ChildWorkflowId = childWorkflowId,
            Total = total,
            NextIndex = 0,
            CompletedCount = 0,
            ItemsJson = itemsJson,
            ResultsJson = slots.ToJsonString(),
            AnyFailed = false,
        };
    }

    public string ItemPayload(int index) => JsonNode.Parse(ItemsJson)!.AsArray()[index]?.ToJsonString() ?? "null";

    public bool IsFilled(int index) => JsonNode.Parse(ResultsJson)!.AsArray()[index] is not null;

    public void TakeNext() => NextIndex++;

    // Records item `index`'s result and bumps the completed count. Idempotent at the call site via
    // IsFilled (a redelivered child-resume is ignored before this runs).
    public void Record(int index, string resultJson, bool failed)
    {
        var slots = JsonNode.Parse(ResultsJson)!.AsArray();
        slots[index] = JsonNode.Parse(resultJson);
        ResultsJson = slots.ToJsonString();
        CompletedCount++;
        if (failed)
        {
            AnyFailed = true;
        }
    }

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
