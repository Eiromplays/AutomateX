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

    public static async Task<List<RunWorkflow>> CollectAsync(
        AutomateXDbContext dbContext,
        EngineOptions options,
        Execution execution,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var triggers = await dbContext.Triggers
            .Where(x => x.Enabled && x.Type == TriggerType)
            .ToListAsync(cancellationToken);

        if (triggers.Count == 0)
        {
            return [];
        }

        var depth = GetChainDepth(execution.TriggerPayload) + 1;
        List<RunWorkflow> messages = [];

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
