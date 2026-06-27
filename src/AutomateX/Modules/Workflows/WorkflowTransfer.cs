using System.Text.Json;
using System.Text.Json.Nodes;
using AutomateX.Modules.Triggers;

namespace AutomateX.Modules.Workflows;

// The portable workflow document. Secrets cannot travel by construction: webhook
// triggers (per-trigger secrets) and chain triggers (instance-local ids) are
// excluded, and connections ride as name references inside step configs — the
// importing instance needs same-named connections.
public static class WorkflowTransfer
{
    public const int FormatVersion = 1;

    public static JsonObject Export(
        string name,
        string? description,
        IReadOnlyList<StepDefinition> steps,
        IReadOnlyList<(string Type, string ConfigJson)> triggers,
        IReadOnlyList<EdgeDefinition>? edges = null,
        bool continueOnFailure = false)
    {
        var stepsArray = new JsonArray(steps
            .Select(step => (JsonNode)new JsonObject
            {
                ["actionType"] = step.ActionType,
                ["name"] = step.Name,
                ["key"] = step.Key,
                ["idempotencyKey"] = step.IdempotencyKey,
                ["config"] = ParseOrEmpty(step.ConfigJson),
            })
            .ToArray());

        var edgesArray = new JsonArray((edges ?? [])
            .Select(edge => (JsonNode)new JsonObject
            {
                ["from"] = edge.FromOrder,
                ["to"] = edge.ToOrder,
                ["label"] = edge.Label,
            })
            .ToArray());

        // Every trigger travels except the two that can't: webhook (its config holds a
        // per-trigger secret) and workflow-chains (instance-local workflow ids).
        var triggersArray = new JsonArray(triggers
            .Where(trigger => trigger.Type is not (TriggerTypes.Webhook or TriggerTypes.Workflow))
            .Select(trigger => (JsonNode)new JsonObject
            {
                ["type"] = trigger.Type,
                ["config"] = PortableTriggerConfig(trigger.Type, trigger.ConfigJson),
            })
            .ToArray());

        return new JsonObject
        {
            ["automatex"] = FormatVersion,
            ["name"] = name,
            ["description"] = description,
            ["continueOnFailure"] = continueOnFailure,
            ["steps"] = stepsArray,
            ["edges"] = edgesArray,
            ["triggers"] = triggersArray,
        };
    }

    public sealed record ImportedTrigger(string Type, string ConfigJson);

    public sealed record ParsedImport(
        string Name,
        string? Description,
        bool ContinueOnFailure,
        IReadOnlyList<StepDefinition> Steps,
        IReadOnlyList<EdgeDefinition> Edges,
        IReadOnlyList<ImportedTrigger> Triggers);

    public static ParsedImport Parse(JsonObject document)
    {
        if (GetInt(document["automatex"]) != FormatVersion)
        {
            throw new InvalidOperationException(
                $"Unsupported document format — expected an AutomateX export with \"automatex\": {FormatVersion}.");
        }

        var name = (document["name"] as JsonValue)?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("The document has no workflow name.");
        }

        var description = document["description"] is JsonValue desc ? desc.GetValue<string>() : null;
        var continueOnFailure = document["continueOnFailure"] is JsonValue cof && cof.TryGetValue<bool>(out var cofValue) && cofValue;

        List<StepDefinition> steps = [];
        foreach (var node in document["steps"] as JsonArray ?? [])
        {
            var actionType = (node?["actionType"] as JsonValue)?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(actionType))
            {
                throw new InvalidOperationException("Every step needs an actionType.");
            }

            var stepName = node?["name"] is JsonValue value ? value.GetValue<string>() : null;
            var stepKey = node?["key"] is JsonValue keyValue ? keyValue.GetValue<string>() : null;
            var idempotencyKey = node?["idempotencyKey"] is JsonValue idemValue ? idemValue.GetValue<string>() : null;
            steps.Add(new StepDefinition(
                actionType, stepName, node?["config"]?.ToJsonString() ?? "{}", stepKey, idempotencyKey));
        }

        // Edges are optional — pre-branching documents simply have none (linear by Order).
        List<EdgeDefinition> edges = [];
        foreach (var node in document["edges"] as JsonArray ?? [])
        {
            var from = GetInt(node?["from"]);
            var to = GetInt(node?["to"]);
            if (from is null || to is null)
            {
                throw new InvalidOperationException("Every edge needs numeric from/to step indexes.");
            }

            if (from < 0 || from >= steps.Count || to < 0 || to >= steps.Count)
            {
                throw new InvalidOperationException($"Edge {from}->{to} references a step that doesn't exist.");
            }

            var label = node?["label"] is JsonValue value && value.TryGetValue<string>(out var parsed) ? parsed : null;
            edges.Add(new EdgeDefinition(from.Value, to.Value, string.IsNullOrWhiteSpace(label) ? null : label));
        }

        // Carry every trigger except the non-portable ones (defensive — export already drops them).
        List<ImportedTrigger> triggers = [];
        foreach (var node in document["triggers"] as JsonArray ?? [])
        {
            var type = (node?["type"] as JsonValue)?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(type) || type is TriggerTypes.Webhook or TriggerTypes.Workflow)
            {
                continue;
            }

            triggers.Add(new ImportedTrigger(type, node?["config"]?.ToJsonString() ?? "{}"));
        }

        return new ParsedImport(name, description, continueOnFailure, steps, edges, triggers);
    }

    // onFailure's watchWorkflowId is an instance-local workflow id — drop it so the export stays
    // portable; the trigger imports as a workspace-wide alert (re-scope it after import if needed).
    private static JsonNode PortableTriggerConfig(string type, string configJson)
    {
        var config = ParseOrEmpty(configJson);
        if (type == TriggerTypes.OnFailure && config is JsonObject obj)
        {
            obj.Remove("watchWorkflowId");
        }

        return config;
    }

    private static JsonNode ParseOrEmpty(string json)
    {
        try
        {
            return JsonNode.Parse(json) ?? new JsonObject();
        }
        catch (JsonException)
        {
            return new JsonObject();
        }
    }

    private static int? GetInt(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<int>(out var parsed) ? parsed : null;
}
