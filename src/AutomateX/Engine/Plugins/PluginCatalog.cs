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
}
