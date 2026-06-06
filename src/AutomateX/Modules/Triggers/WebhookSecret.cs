using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AutomateX.Modules.Triggers;

public sealed record WebhookTriggerConfig(string? Secret);

// Per-trigger capability secrets: generated server-side at trigger creation, shown
// once, validated fixed-time on fire. Webhooks live outside the global API-key gate —
// third-party senders must never hold the instance key.
public static class WebhookSecret
{
    public static string Generate() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();

    // Webhook URLs are long-lived registrations in external systems — the canonical
    // base is whatever the operator declares public (Engine:PublicBaseUrl). Unset =
    // relative, and the UI prefixes its own origin.
    public static string BuildUrl(string? publicBaseUrl, Guid triggerId, string secret) =>
        $"{publicBaseUrl?.TrimEnd('/')}/api/webhooks/{triggerId}?secret={secret}";

    public static (string ConfigJson, string Secret) AddTo(string configJson)
    {
        var node = (string.IsNullOrWhiteSpace(configJson) ? null : JsonNode.Parse(configJson) as JsonObject)
            ?? new JsonObject();
        var secret = Generate();
        node["secret"] = secret;
        return (node.ToJsonString(), secret);
    }

    public static bool Validate(string configJson, string? providedSecret)
    {
        if (string.IsNullOrEmpty(providedSecret))
        {
            return false;
        }

        try
        {
            var config = JsonSerializer.Deserialize<WebhookTriggerConfig>(configJson, JsonSerializerOptions.Web);
            return !string.IsNullOrEmpty(config?.Secret)
                && CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(providedSecret),
                    Encoding.UTF8.GetBytes(config.Secret));
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
