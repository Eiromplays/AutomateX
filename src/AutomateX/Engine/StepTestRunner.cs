using System.Text.Json;
using System.Text.Json.Nodes;
using AutomateX.Database;
using AutomateX.Engine.Actions;
using AutomateX.Engine.Connections;
using AutomateX.Engine.Security;
using AutomateX.Engine.Templating;
using Microsoft.EntityFrameworkCore;

namespace AutomateX.Engine;

public sealed record StepTestResult(bool Ok, JsonElement? Output, string? Error);

// Runs ONE leaf action for real, in isolation — the opt-in second half of per-step dry-run. It resolves
// the (inline) config against a sample context with LIVE connection values, then invokes the action
// once. No execution/step rows, no chaining, retries, idempotency, metrics, or events: a raw single
// call. Control-flow nodes are rejected — they only mean something inside a running workflow.
public sealed class StepTestRunner(
    AutomateXDbContext dbContext,
    ConnectionResolver connectionResolver,
    ActionRegistry actions)
{
    public static readonly IReadOnlySet<string> ControlFlowTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "switch",
        "forEach",
        "wait",
        "workflow.call",
    };

    public async Task<StepTestResult> RunAsync(
        Guid workspaceId,
        Guid workflowId,
        string actionType,
        string configJson,
        IReadOnlyDictionary<string, int> stepKeys,
        JsonElement? triggerPayload,
        IReadOnlyDictionary<int, JsonElement> stepOutputs,
        CancellationToken cancellationToken)
    {
        if (ControlFlowTypes.Contains(actionType))
        {
            return new StepTestResult(false, null, $"'{actionType}' is a control-flow step and can't be test-run on its own.");
        }

        if (!actions.Contains(actionType, workspaceId))
        {
            return new StepTestResult(false, null, $"Unknown action '{actionType}'.");
        }

        var secretSink = new HashSet<string>();
        Dictionary<string, JsonElement>? connections = null;
        if (configJson.Contains("{{connections.", StringComparison.Ordinal))
        {
            var workspaceConnections = await dbContext.Connections
                .AsNoTracking()
                .Where(x => x.WorkspaceId == workspaceId)
                .ToListAsync(cancellationToken);
            connections = await connectionResolver.ResolveAsync(workspaceConnections, cancellationToken);
        }

        var context = new TemplateContext(
            triggerPayload, stepOutputs, Guid.NewGuid(), workflowId, connections, stepKeys, SecretSink: secretSink);

        string resolved;
        try
        {
            resolved = TemplateResolver.Resolve(configJson, context);
        }
        catch (Exception ex) when (ex is TemplateResolutionException or SecretCipherException)
        {
            return new StepTestResult(false, null, ex.Message);
        }

        try
        {
            var invocation = new ActionInvocation(context.ExecutionId, workflowId, StepOrder: 0);
            var output = await actions.Get(actionType, workspaceId).ExecuteAsync(resolved, invocation, cancellationToken);
            return new StepTestResult(true, ParseOutput(output), null);
        }
        catch (Exception ex)
        {
            // Connection secrets are masked in the surfaced error, same as a real execution.
            return new StepTestResult(false, null, SecretMasker.MaskSecrets(ex.Message, secretSink) ?? ex.Message);
        }
    }

    // Actions return JSON when they can; a plain (non-JSON) string is wrapped so the result is always
    // a JsonElement the client can render.
    private static JsonElement? ParseOutput(string? output)
    {
        if (output is null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<JsonElement>(output);
        }
        catch (JsonException)
        {
            return JsonValue.Create(output).Deserialize<JsonElement>();
        }
    }
}
