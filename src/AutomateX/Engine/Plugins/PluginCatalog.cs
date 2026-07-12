using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AutomateX.Engine.Plugins;

// The catalog is a static json published as a release asset; installs verify
// the content hash BEFORE anything touches disk.
public static class PluginCatalog
{
    public sealed record Entry(string Name, string Version, string? Description, string Url, string Sha256);

    public static List<Entry> Parse(string json)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("The catalog is not valid JSON.");
        }

        if (root?["plugins"] is not JsonArray plugins)
        {
            throw new InvalidOperationException("The catalog has no plugins array.");
        }

        List<Entry> entries = [];
        foreach (var node in plugins)
        {
            var name = (node?["name"] as JsonValue)?.GetValue<string>();
            var version = (node?["version"] as JsonValue)?.GetValue<string>();
            var url = (node?["url"] as JsonValue)?.GetValue<string>();
            var sha256 = (node?["sha256"] as JsonValue)?.GetValue<string>();

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(version)
                || string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(sha256))
            {
                throw new InvalidOperationException("A catalog entry is missing name, version, url or sha256.");
            }

            var description = node?["description"] is JsonValue desc ? desc.GetValue<string>() : null;
            entries.Add(new Entry(name, version, description, url, sha256));
        }

        return entries;
    }

    public static bool Verify(byte[] content, string expectedSha256) =>
        string.Equals(
            Convert.ToHexString(SHA256.HashData(content)),
            expectedSha256,
            StringComparison.OrdinalIgnoreCase);

    // The v2 index: manifest fields per plugin plus a newest-first versions[] (each with its own url +
    // sha256 + compatibility). A v1 entry (flat name/version/url/sha256, no versions[]) folds into a
    // single-version entry with a lenient synthesised manifest, so a legacy catalog still parses.
    public static CatalogIndex ParseIndex(string json)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("The catalog is not valid JSON.");
        }

        if (root is not JsonObject rootObject || rootObject["plugins"] is not JsonArray plugins)
        {
            throw new InvalidOperationException("The catalog has no plugins array.");
        }

        var schemaVersion = rootObject["schemaVersion"] is JsonValue schema && schema.TryGetValue<int>(out var value)
            ? value
            : 1;

        CatalogPublisher? publisher = null;
        if (rootObject["publisher"] is JsonObject pub && ManifestJson.Str(pub["name"]) is { } publisherName)
        {
            publisher = new CatalogPublisher(publisherName, ManifestJson.Str(pub["keyId"]));
        }

        List<CatalogPlugin> entries = [];
        foreach (var node in plugins)
        {
            if (node is not JsonObject entry)
            {
                throw new InvalidOperationException("A catalog entry is not an object.");
            }

            entries.Add(entry["versions"] is JsonArray versions
                ? new CatalogPlugin(PluginManifest.FromNode(entry), ParseVersions(versions))
                : LegacyEntry(entry));
        }

        return new CatalogIndex(schemaVersion, publisher, entries);
    }

    private static IReadOnlyList<CatalogVersion> ParseVersions(JsonArray versions)
    {
        List<CatalogVersion> parsed = [];
        foreach (var node in versions)
        {
            var version = ManifestJson.Str(node?["version"]);
            var url = ManifestJson.Str(node?["url"]);
            var sha256 = ManifestJson.Str(node?["sha256"]);
            if (version is null || url is null || sha256 is null)
            {
                throw new InvalidOperationException("A catalog version entry is missing version, url or sha256.");
            }

            if (!ManifestFormats.SemVer().IsMatch(version))
            {
                throw new InvalidOperationException($"Catalog version '{version}' is not a valid semantic version.");
            }

            var minVersion = ManifestJson.Str(node?["minAutomateXVersion"]);
            if (minVersion is not null && !ManifestFormats.SemVer().IsMatch(minVersion))
            {
                throw new InvalidOperationException($"minAutomateXVersion '{minVersion}' is not a valid semantic version.");
            }

            parsed.Add(new CatalogVersion(version, url, sha256, minVersion));
        }

        if (parsed.Count == 0)
        {
            throw new InvalidOperationException("A catalog entry has no versions.");
        }

        return parsed;
    }

    private static CatalogPlugin LegacyEntry(JsonObject node)
    {
        var name = ManifestJson.Str(node["name"]);
        var version = ManifestJson.Str(node["version"]);
        var url = ManifestJson.Str(node["url"]);
        var sha256 = ManifestJson.Str(node["sha256"]);
        if (name is null || version is null || url is null || sha256 is null)
        {
            throw new InvalidOperationException("A catalog entry is missing name, version, url or sha256.");
        }

        return new CatalogPlugin(
            PluginManifest.Legacy(name, version, ManifestJson.Str(node["description"])),
            [new CatalogVersion(version, url, sha256, null)]);
    }
}

public sealed record CatalogPublisher(string Name, string? KeyId);

public sealed record CatalogVersion(string Version, string Url, string Sha256, string? MinAutomateXVersion);

public sealed record CatalogPlugin(PluginManifest Manifest, IReadOnlyList<CatalogVersion> Versions)
{
    // Versions are published newest-first.
    public CatalogVersion Latest => Versions[0];

    public CatalogVersion? Select(string? version) =>
        version is null
            ? Latest
            : Versions.FirstOrDefault(x => string.Equals(x.Version, version, StringComparison.OrdinalIgnoreCase));
}

public sealed record CatalogIndex(int SchemaVersion, CatalogPublisher? Publisher, IReadOnlyList<CatalogPlugin> Plugins);
