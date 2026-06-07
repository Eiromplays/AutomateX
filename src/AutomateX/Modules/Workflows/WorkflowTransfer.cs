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
        IReadOnlyList<(string Type, string ConfigJson)> triggers)
    {
        var stepsArray = new JsonArray(steps
            .Select(step => (JsonNode)new JsonObject
            {
                ["actionType"] = step.ActionType,
                ["name"] = step.Name,
                ["config"] = ParseOrEmpty(step.ConfigJson),
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
            ["triggers"] = triggersArray,
        };
    }

    public sealed record ParsedImport(
        string Name,
        string? Description,
        IReadOnlyList<StepDefinition> Steps,
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

        List<string> crons = [];
        foreach (var node in document["triggers"] as JsonArray ?? [])
        {
            if ((node?["type"] as JsonValue)?.GetValue<string>() == TriggerTypes.Cron)
            {
                crons.Add(node?["config"]?.ToJsonString() ?? "{}");
            }
        }

        return new ParsedImport(name, description, steps, crons);
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
