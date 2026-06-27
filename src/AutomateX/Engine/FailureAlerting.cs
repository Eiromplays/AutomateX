using System.Text.Json;
using System.Text.Json.Nodes;
using AutomateX.Database;
using AutomateX.Modules.Executions;
using AutomateX.Modules.Triggers;
using Microsoft.EntityFrameworkCore;

namespace AutomateX.Engine;

// "execution.onFailure" triggers: a workspace-wide failure subscriber. When an execution settles
// Failed, every enabled onFailure trigger in the same workspace starts its workflow with a rich
// failure summary as {{trigger.payload}}. Collected on the durable terminal-site path (via
// WorkflowChaining.CollectAsync) so an alert can't be lost to a crash. Loop-guarded: an alert run
// never alerts (self-exclusion by TriggeredBy), and sub-workflow/forEach children are suppressed by
// default (their failure surfaces on the parent, which fails and alerts once).
public static class FailureAlerting
{
    public sealed record OnFailureConfig(Guid? WatchWorkflowId = null, bool IncludeSubWorkflows = false);

    public static async Task<List<object>> CollectAsync(
        AutomateXDbContext dbContext,
        EngineOptions options,
        Execution execution,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        // Only failures, and never alert on an alert run — the guard that makes this loop-safe.
        if (execution.Status != ExecutionStatus.Failed || execution.TriggeredBy == TriggerTypes.OnFailure)
        {
            return [];
        }

        var triggers = await dbContext.Triggers
            .Where(x => x.Enabled && x.Type == TriggerTypes.OnFailure)
            .ToListAsync(cancellationToken);

        if (triggers.Count == 0)
        {
            return [];
        }

        var depth = WorkflowChaining.GetChainDepth(execution.TriggerPayload) + 1;
        var payload = await BuildPayloadAsync(dbContext, options, execution, depth, cancellationToken);

        List<object> messages = [];
        foreach (var trigger in triggers)
        {
            OnFailureConfig config;
            try
            {
                config = JsonSerializer.Deserialize<OnFailureConfig>(trigger.ConfigJson, JsonSerializerOptions.Web)
                    ?? new OnFailureConfig();
            }
            catch (JsonException)
            {
                config = new OnFailureConfig();
            }

            // Children surface on their parent — suppress unless this trigger opts in.
            if (execution.Depth > 0 && !config.IncludeSubWorkflows)
            {
                continue;
            }

            if (config.WatchWorkflowId is { } watched && watched != execution.WorkflowId)
            {
                continue;
            }

            // Workspace boundary: the alert workflow must live where the failed run did.
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
                    "onFailure depth {Depth} exceeds MaxChainDepth {Max} — not alerting workflow {WorkflowId} for execution {ExecutionId}",
                    depth, options.MaxChainDepth, trigger.WorkflowId, execution.Id);
                continue;
            }

            trigger.MarkFired(trigger.NextRunAt);
            messages.Add(new RunWorkflow(
                Guid.CreateVersion7(), trigger.WorkflowId, TriggerTypes.OnFailure, payload, trigger.EntryStepOrder));
        }

        return messages;
    }

    private static async Task<string> BuildPayloadAsync(
        AutomateXDbContext dbContext,
        EngineOptions options,
        Execution execution,
        int depth,
        CancellationToken cancellationToken)
    {
        var workflowName = await dbContext.Workflows
            .Where(x => x.Id == execution.WorkflowId)
            .Select(x => x.Name)
            .FirstOrDefaultAsync(cancellationToken);

        var failed = execution.Steps
            .Where(s => s.Status == ExecutionStatus.Failed)
            .OrderBy(s => s.StepOrder)
            .FirstOrDefault();

        JsonNode? failedStep = null;
        if (failed is not null)
        {
            var key = await dbContext.WorkflowSteps
                .Where(s => s.WorkflowVersionId == execution.WorkflowVersionId && s.Order == failed.StepOrder)
                .Select(s => s.Key)
                .FirstOrDefaultAsync(cancellationToken);

            failedStep = new JsonObject
            {
                ["order"] = failed.StepOrder,
                ["key"] = key,
                ["actionType"] = failed.ActionType,
                ["error"] = failed.Error,
            };
        }

        return new JsonObject
        {
            ["chainDepth"] = depth,
            ["executionId"] = execution.Id.ToString(),
            ["workflowId"] = execution.WorkflowId.ToString(),
            ["workflowName"] = workflowName,
            ["status"] = execution.Status.ToString(),
            ["failedStep"] = failedStep,
            ["startedAt"] = execution.StartedAt.ToString("o"),
            ["completedAt"] = execution.CompletedAt?.ToString("o"),
            ["url"] = options.PublicBaseUrl is { Length: > 0 } baseUrl
                ? $"{baseUrl.TrimEnd('/')}/executions/{execution.Id}"
                : null,
        }.ToJsonString();
    }
}
