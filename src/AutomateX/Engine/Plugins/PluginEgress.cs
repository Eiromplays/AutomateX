using System.Text.Json;
using System.Text.Json.Nodes;

namespace AutomateX.Engine.Plugins;

// Derives a plugin's restrictive egress allowlist from its manifest's declared permissions.egress. Specific
// declared hosts restrict the plugin to exactly those; a wildcard ("*"), an empty list, or no manifest means
// "unrestricted" (empty result) — so a plugin only gets locked down when it (and the operator, at install)
// opted in by declaring concrete hosts.
public static class PluginEgress
{
    public static IReadOnlyList<string> AllowOnly(IReadOnlyList<string> declaredEgress) =>
        declaredEgress.Count == 0 || declaredEgress.Any(x => x == "*")
            ? []
            : declaredEgress;

    // Reads plugin.json next to the plugin dll and returns its restrictive allowlist. Lenient: a missing or
    // unreadable manifest yields no restriction, so a dev-dropped or manifest-less plugin behaves as today.
    public static IReadOnlyList<string> ForPlugin(string pluginDll)
    {
        try
        {
            var manifestPath = Path.Combine(Path.GetDirectoryName(pluginDll)!, "plugin.json");
            if (!File.Exists(manifestPath))
            {
                return [];
            }

            var egress = (JsonNode.Parse(File.ReadAllText(manifestPath))?["permissions"]?["egress"] as JsonArray)?
                .Select(x => (x as JsonValue)?.GetValue<string>()?.Trim())
                .Where(x => !string.IsNullOrEmpty(x))
                .Select(x => x!)
                .ToList() ?? [];

            return AllowOnly(egress);
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or JsonException
                or ArgumentException or InvalidOperationException)
        {
            return [];
        }
    }
}
