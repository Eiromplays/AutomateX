using System.Text.Json;
using System.Text.Json.Nodes;

namespace AutomateX.Modules.Templates;

// The community template catalog: a static catalog.json published as a release asset. Templates are
// small, inert JSON workflow docs, so they're inlined directly in the catalog (no separate download or
// hash gate — unlike plugins, which are code). Integrity rides on HTTPS to the release.
public static class TemplateCatalog
{
    public sealed record Entry(string Name, string? Description, string? Category, JsonNode Doc);

    public static List<Entry> Parse(string json)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("The template catalog is not valid JSON.");
        }

        if (root?["templates"] is not JsonArray templates)
        {
            throw new InvalidOperationException("The template catalog has no templates array.");
        }

        List<Entry> entries = [];
        foreach (var node in templates)
        {
            var name = (node?["name"] as JsonValue)?.GetValue<string>();
            var doc = node?["doc"];
            if (string.IsNullOrWhiteSpace(name) || doc is null)
            {
                throw new InvalidOperationException("A catalog entry is missing name or doc.");
            }

            var description = (node?["description"] as JsonValue)?.GetValue<string>();
            var category = (node?["category"] as JsonValue)?.GetValue<string>();
            entries.Add(new Entry(name, description, category, doc.DeepClone()));
        }

        return entries;
    }
}
