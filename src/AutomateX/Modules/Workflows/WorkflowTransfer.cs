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
        IReadOnlyList<EdgeDefinition>? edges = null)
    {
        var stepsArray = new JsonArray(steps
            .Select(step => (JsonNode)new JsonObject
            {
                ["actionType"] = step.ActionType,
                ["name"] = step.Name,
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

        var triggersArray = new JsonArray(triggers
            .Where(trigger => trigger.Type == TriggerTypes.Cron)
            .Select(trigger => (JsonNode)new JsonObject
            {
                ["type"] = trigger.Type,
                ["config"] = ParseOrEmpty(trigger.ConfigJson),
            })
            .ToArray());

        return new JsonObject
        {
            ["automatex"] = FormatVersion,
            ["name"] = name,
            ["description"] = description,
            ["steps"] = stepsArray,
            ["edges"] = edgesArray,
            ["triggers"] = triggersArray,
        };
    }

    public sealed record ParsedImport(
        string Name,
        string? Description,
        IReadOnlyList<StepDefinition> Steps,
        IReadOnlyList<EdgeDefinition> Edges,
        IReadOnlyList<string> CronTriggerConfigs);

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

        List<StepDefinition> steps = [];
        foreach (var node in document["steps"] as JsonArray ?? [])
        {
            var actionType = (node?["actionType"] as JsonValue)?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(actionType))
            {
                throw new InvalidOperationException("Every step needs an actionType.");
            }

            var stepName = node?["name"] is JsonValue value ? value.GetValue<string>() : null;
            steps.Add(new StepDefinition(actionType, stepName, node?["config"]?.ToJsonString() ?? "{}"));
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

        List<string> crons = [];
        foreach (var node in document["triggers"] as JsonArray ?? [])
        {
            if ((node?["type"] as JsonValue)?.GetValue<string>() == TriggerTypes.Cron)
            {
                crons.Add(node?["config"]?.ToJsonString() ?? "{}");
            }
        }

        return new ParsedImport(name, description, steps, edges, crons);
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
