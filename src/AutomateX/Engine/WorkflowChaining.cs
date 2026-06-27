using System.Text.Json;
using System.Text.Json.Nodes;
using AutomateX.Database;
using AutomateX.Modules.Executions;
using Microsoft.EntityFrameworkCore;

namespace AutomateX.Engine;

// Workflow chaining: "workflow" triggers fire their workflow when the watched
// workflow reaches a terminal state. Collected RunWorkflow messages are returned
// as Wolverine cascades from the step handler, so they commit through the same
// outbox as everything else — chains survive crashes like steps do.
public static class WorkflowChaining
{
    public const string TriggerType = "workflow";

    public sealed record ChainConfig(Guid WorkflowId, string On = "succeeded");

    public static bool ShouldFire(string on, ExecutionStatus status) => on switch
    {
        "any" => status is ExecutionStatus.Succeeded or ExecutionStatus.Failed,
        "failed" => status == ExecutionStatus.Failed,
        _ => status == ExecutionStatus.Succeeded,
    };

    // The parent execution id a chained payload carries — lineage for read models.
    public static Guid? GetSourceExecutionId(string? triggerPayload)
    {
        if (string.IsNullOrWhiteSpace(triggerPayload))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(triggerPayload)?["source"]?["executionId"] is JsonValue value
                && Guid.TryParse(value.GetValue<string>(), out var parsed)
                    ? parsed
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static int GetChainDepth(string? triggerPayload)
    {
        if (string.IsNullOrWhiteSpace(triggerPayload))
        {
            return 0;
        }

        try
        {
            return JsonNode.Parse(triggerPayload) is JsonObject root
                && root.TryGetPropertyValue("chainDepth", out var depth)
                && depth is JsonValue value
                && value.TryGetValue<int>(out var parsed)
                    ? parsed
                    : 0;
        }
        catch (JsonException)
        {
            return 0;
        }
    }

    // Collected at every terminal site and cascaded through the outbox: the "workflow" triggers that
    // should fire now, plus — if this run is a sub-workflow call — a ResumeExecution that wakes the
    // waiting parent with the child's result. Both ride the durable outbox, crash-safe.
    public static async Task<List<object>> CollectAsync(
        AutomateXDbContext dbContext,
        EngineOptions options,
        Execution execution,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        List<object> messages = [];

        // Sub-workflow parent wakeup (durable, idempotent via ResumeExecution's atomic claim).
        if (execution.ParentExecutionId is { } parentId && execution.ParentStepOrder is { } parentOrder)
        {
            messages.Add(new ResumeExecution(parentId, parentOrder, "child", BuildChildResult(execution), execution.ParentItemIndex));
        }

        // execution.onFailure alert workflows ride the same durable outbox (no-op unless this run failed).
        messages.AddRange(await FailureAlerting.CollectAsync(dbContext, options, execution, logger, cancellationToken));

        var triggers = await dbContext.Triggers
            .Where(x => x.Enabled && x.Type == TriggerType)
            .ToListAsync(cancellationToken);

        if (triggers.Count == 0)
        {
            return messages;
        }

        var depth = GetChainDepth(execution.TriggerPayload) + 1;

        foreach (var trigger in triggers)
        {
            ChainConfig? config;
            try
            {
                config = JsonSerializer.Deserialize<ChainConfig>(trigger.ConfigJson, JsonSerializerOptions.Web);
            }
            catch (JsonException)
            {
                continue;
            }

            if (config is null || config.WorkflowId != execution.WorkflowId
                || !ShouldFire(config.On, execution.Status))
            {
                continue;
            }

            // Workspace boundary: the chained workflow must live where the source ran.
            var targetWorkspace = await dbContext.Workflows
                .Where(x => x.Id == trigger.WorkflowId)
                .Select(x => (Guid?)x.WorkspaceId)
                .FirstOrDefaultAsync(cancellationToken);

            if (targetWorkspace != execution.WorkspaceId)
            {
                continue;
            }

            if (depth > options.MaxChainDepth)
            {
                logger.LogWarning(
                    "Chain depth {Depth} exceeds MaxChainDepth {Max} — not firing workflow {WorkflowId} from execution {ExecutionId}",
                    depth, options.MaxChainDepth, trigger.WorkflowId, execution.Id);
                continue;
            }

            trigger.MarkFired(trigger.NextRunAt);
            messages.Add(new RunWorkflow(
                Guid.CreateVersion7(), trigger.WorkflowId, TriggerType, BuildPayload(execution, depth), trigger.EntryStepOrder));
        }

        return messages;
    }

    // The child run's result handed back to a waiting parent (workflow.call step output): the final
    // status, the child id, and the highest-order succeeded step's output as a best-effort return.
    private static string BuildChildResult(Execution execution)
    {
        var last = execution.Steps
            .Where(s => s.Status == ExecutionStatus.Succeeded)
            .OrderByDescending(s => s.StepOrder)
            .FirstOrDefault();

        JsonNode? output = null;
        if (last?.Output is { Length: > 0 } raw)
        {
            try
            {
                output = JsonNode.Parse(raw);
            }
            catch (JsonException)
            {
                output = JsonValue.Create(raw);
            }
        }

        return new JsonObject
        {
            ["status"] = execution.Status.ToString(),
            ["executionId"] = execution.Id.ToString(),
            ["output"] = output,
        }.ToJsonString();
    }

    private static string BuildPayload(Execution source, int depth)
    {
        JsonNode? sourcePayload = null;
        if (source.TriggerPayload is { Length: > 0 } raw)
        {
            try
            {
                sourcePayload = JsonNode.Parse(raw);
            }
            catch (JsonException)
            {
                sourcePayload = JsonValue.Create(raw);
            }
        }

        return new JsonObject
        {
            ["chainDepth"] = depth,
            ["source"] = new JsonObject
            {
                ["workflowId"] = source.WorkflowId.ToString(),
                ["executionId"] = source.Id.ToString(),
                ["status"] = source.Status.ToString(),
                ["triggerPayload"] = sourcePayload,
            },
        }.ToJsonString();
    }
}
