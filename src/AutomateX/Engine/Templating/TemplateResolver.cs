using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace AutomateX.Engine.Templating;

public sealed class TemplateResolutionException(string message) : Exception(message);

public sealed record TemplateContext(
    JsonElement? TriggerPayload,
    IReadOnlyDictionary<int, JsonElement> StepOutputs,
    Guid ExecutionId,
    Guid WorkflowId,
    IReadOnlyDictionary<string, JsonElement>? Connections = null,
    IReadOnlyDictionary<string, int>? StepKeys = null,
    IReadOnlyDictionary<int, JsonElement>? StepErrors = null,
    ISet<string>? SecretSink = null,
    // Workspace/workflow variables resolved for this run ({{vars.<name>}}); SecretVariableNames drives
    // masking, the same way connection field reads are masked.
    IReadOnlyDictionary<string, string>? Variables = null,
    IReadOnlySet<string>? SecretVariableNames = null,
    // Preview mode: when set, an unresolvable path is recorded here and rendered as a placeholder
    // instead of throwing — so a dry-run can report *every* miss in one pass. Null = strict (execution).
    ICollection<string>? UnresolvedSink = null);

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

    // Resolves {{tokens}} in a plain (non-JSON) string to a flat string — for fields like an
    // idempotency key that aren't JSON. A whole-string token keeps its resolved scalar (stringified);
    // a null/missing value resolves to null. Unresolvable paths throw, like Resolve.
    public static string? ResolveString(string template, TemplateContext context) => ResolveText(template, context) switch
    {
        null => null,
        JsonValue value when value.TryGetValue<string>(out var text) => text,
        var node => node.ToJsonString(),
    };

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
            var path = matches[0].Groups[1].Value;
            if (!TryResolvePath(path, context, out var element))
            {
                return JsonValue.Create(Placeholder(path));
            }

            return element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
                ? null
                : JsonNode.Parse(element.GetRawText());
        }

        var interpolated = Token().Replace(text, match =>
            TryResolvePath(match.Groups[1].Value, context, out var value)
                ? Stringify(value)
                : Placeholder(match.Groups[1].Value));
        return JsonValue.Create(interpolated);
    }

    private static string Placeholder(string path) => $"[unresolved: {path}]";

    // Strict (UnresolvedSink null): resolve or throw, unchanged. Preview: catch the miss, record the
    // path, and signal the caller to substitute a placeholder.
    private static bool TryResolvePath(string path, TemplateContext context, out JsonElement element)
    {
        if (context.UnresolvedSink is null)
        {
            element = ResolvePath(path, context);
            return true;
        }

        try
        {
            element = ResolvePath(path, context);
            return true;
        }
        catch (TemplateResolutionException)
        {
            context.UnresolvedSink.Add(path);
            element = default;
            return false;
        }
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
        var (current, consumed, isSecret) = ResolveRoot(segments, path, context);

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

        if (isSecret)
        {
            context.SecretSink?.Add(Stringify(current));
        }

        return current;
    }

    // A numeric segment is a step order; anything else is a step key resolved via StepKeys.
    private static int ResolveStepOrder(string segment, string path, TemplateContext context)
    {
        if (int.TryParse(segment, out var order))
        {
            return order;
        }

        return context.StepKeys is { } keys && keys.TryGetValue(segment, out var keyed)
            ? keyed
            : throw new TemplateResolutionException(
                $"Path '{path}' could not be resolved: unknown step '{segment}'.");
    }

    private static (JsonElement Root, int ConsumedSegments, bool IsSecret) ResolveRoot(
        string[] segments, string path, TemplateContext context)
    {
        switch (segments[0])
        {
            case "trigger" when segments.Length >= 2 && segments[1] == "payload":
                return context.TriggerPayload is { } payload
                    ? (payload, 2, false)
                    : throw new TemplateResolutionException(
                        $"Path '{path}' could not be resolved: this execution has no trigger payload.");

            case "steps" when segments.Length >= 3 && segments[2] is "output" or "error":
                var order = ResolveStepOrder(segments[1], path, context);
                if (segments[2] == "error")
                {
                    return context.StepErrors is { } errors && errors.TryGetValue(order, out var failure)
                        ? (failure, 3, false)
                        : throw new TemplateResolutionException(
                            $"Path '{path}' could not be resolved: step '{segments[1]}' has no error (it didn't fail).");
                }

                return context.StepOutputs.TryGetValue(order, out var output)
                    ? (output, 3, false)
                    : throw new TemplateResolutionException(
                        $"Path '{path}' could not be resolved: no completed step '{segments[1]}'.");

            case "execution" when segments.Length == 2 && segments[1] == "id":
                return (JsonSerializer.SerializeToElement(context.ExecutionId.ToString()), 2, false);

            case "workflow" when segments.Length == 2 && segments[1] == "id":
                return (JsonSerializer.SerializeToElement(context.WorkflowId.ToString()), 2, false);

            case "vars" when segments.Length >= 2:
                return context.Variables is not null && context.Variables.TryGetValue(segments[1], out var variable)
                    ? (JsonSerializer.SerializeToElement(variable), 2, context.SecretVariableNames?.Contains(segments[1]) == true)
                    : throw new TemplateResolutionException(
                        $"Path '{path}' could not be resolved: unknown variable '{segments[1]}'.");

            case "connections" when segments.Length >= 3:
                return context.Connections is not null
                    && context.Connections.TryGetValue(segments[1], out var secrets)
                    ? (secrets, 2, true)
                    : throw new TemplateResolutionException(
                        $"Path '{path}' could not be resolved: unknown connection '{segments[1]}'.");

            default:
                throw new TemplateResolutionException(
                    $"Path '{path}' could not be resolved: unknown root '{segments[0]}'. " +
                    "Supported: trigger.payload, steps.<order>.output, connections.<name>.<field>, vars.<name>, execution.id, workflow.id.");
        }
    }
}
