using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace AutomateX.Engine.Plugins;

// A plugin's own schema (`plugin.json`). Authored and strictly validated. Carries the plugin's independent
// semver (decoupled from the API release), declared capabilities (shown in the catalog before install), and
// required permissions (consented at install, enforced at runtime). Legacy v1 catalog entries have no
// authored manifest, so ParseIndex synthesises a lenient shim via Legacy().
public sealed record PluginManifest(
    string Id,
    string Name,
    string DisplayName,
    string Version,
    string? Description,
    string? Author,
    string? Homepage,
    string? License,
    IReadOnlyList<string> Categories,
    string? Icon,
    string? MinAutomateXVersion,
    PluginCapabilities Capabilities,
    PluginPermissions Permissions)
{
    public static PluginManifest Parse(string json)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("The plugin manifest is not valid JSON.");
        }

        if (root is not JsonObject node)
        {
            throw new InvalidOperationException("The plugin manifest must be a JSON object.");
        }

        return FromNode(node);
    }

    // Reads + validates the manifest fields off a JSON object — a standalone plugin.json or a v2 catalog
    // plugin entry (the manifest fields are inline on the entry).
    internal static PluginManifest FromNode(JsonObject node)
    {
        var id = ManifestJson.Str(node["id"]) ?? throw Missing("id");
        if (!ManifestFormats.Id().IsMatch(id))
        {
            throw new InvalidOperationException($"Plugin id '{id}' must be reverse-DNS (e.g. 'automatex.slack').");
        }

        var name = ManifestJson.Str(node["name"]) ?? throw Missing("name");
        PluginArchive.ValidateName(name);

        var displayName = ManifestJson.Str(node["displayName"]) ?? throw Missing("displayName");
        var version = ManifestJson.Str(node["version"]) ?? throw Missing("version");
        RequireSemVer(version, "version");

        var minVersion = ManifestJson.Str(node["minAutomateXVersion"]);
        if (minVersion is not null)
        {
            RequireSemVer(minVersion, "minAutomateXVersion");
        }

        return new PluginManifest(
            id, name, displayName, version,
            ManifestJson.Str(node["description"]), ManifestJson.Str(node["author"]),
            ManifestJson.Str(node["homepage"]), ManifestJson.Str(node["license"]),
            ManifestJson.StrArray(node["categories"]), ManifestJson.Str(node["icon"]), minVersion,
            PluginCapabilities.FromNode(node["capabilities"]), PluginPermissions.FromNode(node["permissions"]));
    }

    // A v1 catalog entry has no authored manifest; derive a lenient shim so downstream always has an
    // id/displayName. Not strictly validated — the fields come from a trusted legacy catalog, not an author.
    internal static PluginManifest Legacy(string name, string version, string? description) =>
        new(name.ToLowerInvariant(), name, name, version, description, null, null, null,
            [], null, null, PluginCapabilities.None, PluginPermissions.None);

    private static InvalidOperationException Missing(string field) =>
        new($"The plugin manifest is missing '{field}'.");

    private static void RequireSemVer(string value, string field)
    {
        if (!ManifestFormats.SemVer().IsMatch(value))
        {
            throw new InvalidOperationException($"'{field}' ('{value}') is not a valid semantic version.");
        }
    }
}

public sealed record PluginCapabilities(
    IReadOnlyList<string> Actions,
    IReadOnlyList<string> Triggers,
    IReadOnlyList<string> ConnectionTypes)
{
    public static readonly PluginCapabilities None = new([], [], []);

    internal static PluginCapabilities FromNode(JsonNode? node) =>
        node is JsonObject obj
            ? new PluginCapabilities(
                ManifestJson.StrArray(obj["actions"]),
                ManifestJson.StrArray(obj["triggers"]),
                ManifestJson.StrArray(obj["connectionTypes"]))
            : None;
}

public sealed record PluginPermissions(IReadOnlyList<string> Egress)
{
    public static readonly PluginPermissions None = new([]);

    internal static PluginPermissions FromNode(JsonNode? node) =>
        node is JsonObject obj ? new PluginPermissions(ManifestJson.StrArray(obj["egress"])) : None;
}

internal static class ManifestJson
{
    public static string? Str(JsonNode? node)
    {
        var value = (node as JsonValue)?.GetValue<string>()?.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    public static IReadOnlyList<string> StrArray(JsonNode? node) =>
        node is JsonArray array
            ? array.Select(Str).Where(x => x is not null).Select(x => x!).ToList()
            : [];
}

internal static partial class ManifestFormats
{
    // Reverse-DNS: lowercase segments (letter-led, alnum/hyphen), at least two of them.
    [GeneratedRegex(@"^[a-z][a-z0-9-]*(?:\.[a-z][a-z0-9-]*)+$")]
    public static partial Regex Id();

    // SemVer 2.0.0 core + optional pre-release / build metadata.
    [GeneratedRegex(@"^\d+\.\d+\.\d+(?:-[0-9A-Za-z-.]+)?(?:\+[0-9A-Za-z-.]+)?$")]
    public static partial Regex SemVer();
}
