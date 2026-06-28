using System.Text.Json;
using System.Text.RegularExpressions;

namespace AutomateX.Engine.Templating;

public sealed record ConnectionUsage(string Name, IReadOnlyList<string> Fields);

public sealed record StepPreviewResult(
    string ResolvedConfig,
    IReadOnlyList<string> Unresolved,
    IReadOnlyList<ConnectionUsage> ConnectionsUsed);

// Pure core of per-step preview: resolve a step's templated config against a supplied context, in
// tolerant mode, so the result reports every unresolved reference at once. Connection *values* are
// masked before resolution — a preview shows which connection fields a step reads, never the secrets.
public static partial class StepPreview
{
    private const string Mask = "******";

    [GeneratedRegex(@"\{\{\s*connections\.([^.}\s]+)\.([^.}\s]+)[^}]*\}\}")]
    private static partial Regex ConnectionRef();

    public static StepPreviewResult Build(
        string configJson,
        JsonElement? triggerPayload,
        IReadOnlyDictionary<int, JsonElement> stepOutputs,
        IReadOnlyDictionary<string, int> stepKeys,
        IReadOnlyDictionary<string, IReadOnlyList<string>> connectionFields,
        IReadOnlyDictionary<string, string> variables,
        Guid workflowId)
    {
        // Connections exist (live) but every value is masked, so resolution can succeed without the
        // resolved config ever carrying a secret.
        var connections = connectionFields.ToDictionary(
            x => x.Key,
            x => JsonSerializer.SerializeToElement(x.Value.ToDictionary(field => field, _ => Mask)));

        var unresolved = new List<string>();
        var context = new TemplateContext(
            triggerPayload,
            stepOutputs,
            ExecutionId: Guid.Empty,
            WorkflowId: workflowId,
            Connections: connections,
            StepKeys: stepKeys,
            UnresolvedSink: unresolved,
            Variables: variables);

        var resolved = TemplateResolver.Resolve(configJson, context);
        return new StepPreviewResult(resolved, Distinct(unresolved), ConnectionsUsed(configJson));
    }

    private static IReadOnlyList<string> Distinct(IEnumerable<string> values)
    {
        HashSet<string> seen = [];
        List<string> ordered = [];
        foreach (var value in values)
        {
            if (seen.Add(value))
            {
                ordered.Add(value);
            }
        }

        return ordered;
    }

    private static IReadOnlyList<ConnectionUsage> ConnectionsUsed(string configJson)
    {
        Dictionary<string, List<string>> byName = [];
        foreach (Match match in ConnectionRef().Matches(configJson))
        {
            var name = match.Groups[1].Value;
            var field = match.Groups[2].Value;
            var fields = byName.TryGetValue(name, out var existing) ? existing : byName[name] = [];
            if (!fields.Contains(field))
            {
                fields.Add(field);
            }
        }

        return [.. byName.Select(x => new ConnectionUsage(x.Key, x.Value))];
    }
}
