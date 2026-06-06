using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace AutomateX.Engine.Templating;

public sealed class TemplateResolutionException(string message) : Exception(message);

public sealed record TemplateContext(
    JsonElement? TriggerPayload,
    IReadOnlyDictionary<int, JsonElement> StepOutputs,
    Guid ExecutionId,
    Guid WorkflowId);

// Resolves {{path}} tokens in step configs before execution. Roots:
//   trigger.payload[.x.y]   steps.<order>.output[.x.y]   execution.id   workflow.id
// A token that is the entire string keeps the resolved JSON type; tokens inside
// larger strings interpolate. Unresolvable paths throw — deterministic errors fail
// the step immediately, with no retries.
public static partial class TemplateResolver
{
    [GeneratedRegex(@"\{\{\s*([A-Za-z0-9_\.\-]+)\s*\}\}")]
    private static partial Regex Token();

    public static string Resolve(string configJson, TemplateContext context)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(configJson);
        }
        catch (JsonException ex)
        {
            throw new TemplateResolutionException($"Step config is not valid JSON: {ex.Message}");
        }

        return ResolveNode(root, context)?.ToJsonString() ?? "null";
    }

    private static JsonNode? ResolveNode(JsonNode? node, TemplateContext context) => node switch
    {
        JsonObject obj => ResolveObject(obj, context),
        JsonArray array => ResolveArray(array, context),
        JsonValue value when value.TryGetValue<string>(out var text) => ResolveText(text, context),
        _ => node?.DeepClone(),
    };

    private static JsonObject ResolveObject(JsonObject obj, TemplateContext context)
    {
        var result = new JsonObject();
        foreach (var (key, value) in obj)
        {
            result[key] = ResolveNode(value, context);
        }

        return result;
    }

    private static JsonArray ResolveArray(JsonArray array, TemplateContext context)
    {
        var result = new JsonArray();
        foreach (var item in array)
        {
            result.Add(ResolveNode(item, context));
        }

        return result;
    }

    private static JsonNode? ResolveText(string text, TemplateContext context)
    {
        var matches = Token().Matches(text);
        if (matches.Count == 0)
        {
            return JsonValue.Create(text);
        }

        if (matches.Count == 1 && matches[0].Index == 0 && matches[0].Length == text.Length)
        {
            var element = ResolvePath(matches[0].Groups[1].Value, context);
            return element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
                ? null
                : JsonNode.Parse(element.GetRawText());
        }

        var interpolated = Token().Replace(text, match => Stringify(ResolvePath(match.Groups[1].Value, context)));
        return JsonValue.Create(interpolated);
    }

    private static string Stringify(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString() ?? "",
        JsonValueKind.Null or JsonValueKind.Undefined => "",
        _ => element.GetRawText(),
    };

    private static JsonElement ResolvePath(string path, TemplateContext context)
    {
        var segments = path.Split('.');
        var (current, consumed) = ResolveRoot(segments, path, context);

        foreach (var segment in segments.Skip(consumed))
        {
            current = current.ValueKind switch
            {
                JsonValueKind.Object when current.TryGetProperty(segment, out var property) => property,
                JsonValueKind.Array when int.TryParse(segment, out var index)
                    && index >= 0 && index < current.GetArrayLength() => current[index],
                _ => throw new TemplateResolutionException(
                    $"Path '{path}' could not be resolved: segment '{segment}' not found."),
            };
        }

        return current;
    }

    private static (JsonElement Root, int ConsumedSegments) ResolveRoot(
        string[] segments, string path, TemplateContext context)
    {
        switch (segments[0])
        {
            case "trigger" when segments.Length >= 2 && segments[1] == "payload":
                return context.TriggerPayload is { } payload
                    ? (payload, 2)
                    : throw new TemplateResolutionException(
                        $"Path '{path}' could not be resolved: this execution has no trigger payload.");

            case "steps" when segments.Length >= 3
                && int.TryParse(segments[1], out var order)
                && segments[2] == "output":
                return context.StepOutputs.TryGetValue(order, out var output)
                    ? (output, 3)
                    : throw new TemplateResolutionException(
                        $"Path '{path}' could not be resolved: no completed step with order {order}.");

            case "execution" when segments.Length == 2 && segments[1] == "id":
                return (JsonSerializer.SerializeToElement(context.ExecutionId.ToString()), 2);

            case "workflow" when segments.Length == 2 && segments[1] == "id":
                return (JsonSerializer.SerializeToElement(context.WorkflowId.ToString()), 2);

            default:
                throw new TemplateResolutionException(
                    $"Path '{path}' could not be resolved: unknown root '{segments[0]}'. " +
                    "Supported: trigger.payload, steps.<order>.output, execution.id, workflow.id.");
        }
    }
}
