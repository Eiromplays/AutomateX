using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AutomateX.Modules.Triggers;

public sealed record WebhookTriggerConfig(string? Secret);

// Per-trigger capability secrets: generated server-side at creation, shown once, never stored
// retrievably. Senders authenticate with an HMAC-SHA256 signature of the raw body
// (X-Webhook-Signature: sha256=<hex>) — nothing secret ever travels in the URL. The plaintext
// secret in X-Webhook-Secret is also accepted (fine over HTTPS; handy for manual calls).
public static class WebhookSecret
{
    public const string SignatureHeader = "X-Webhook-Signature";
    public const string SecretHeader = "X-Webhook-Secret";

    public static string Generate() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();

    // Webhook URLs are long-lived registrations in external systems — the canonical base is
    // whatever the operator declares public (Engine:PublicBaseUrl). Unset = relative, and the UI
    // prefixes its own origin. The secret is never in the URL (it's shown once at creation).
    public static string BuildUrl(string? publicBaseUrl, Guid triggerId) =>
        $"{publicBaseUrl?.TrimEnd('/')}/api/webhooks/{triggerId}";

    public static (string ConfigJson, string Secret) AddTo(string configJson)
    {
        var node = (string.IsNullOrWhiteSpace(configJson) ? null : JsonNode.Parse(configJson) as JsonObject)
            ?? new JsonObject();
        var secret = Generate();
        node["secret"] = secret;
        return (node.ToJsonString(), secret);
    }

    // HMAC-SHA256 of the body keyed by the secret, formatted "sha256=<lowercase hex>" (GitHub/Stripe
    // convention). What a sender computes and puts in X-Webhook-Signature.
    public static string Sign(string secret, string body) =>
        "sha256=" + Convert.ToHexString(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body))).ToLowerInvariant();

    // Accepts an HMAC signature over the raw body (preferred) or the plaintext secret header.
    // Both compared fixed-time; a missing stored secret (legacy trigger) always fails.
    public static bool Validate(string configJson, string requestBody, string? signature, string? plaintextSecret)
    {
        var secret = SecretOf(configJson);
        if (string.IsNullOrEmpty(secret))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(signature))
        {
            return FixedTimeEquals(signature.Trim().ToLowerInvariant(), Sign(secret, requestBody));
        }

        if (!string.IsNullOrEmpty(plaintextSecret))
        {
            return FixedTimeEquals(plaintextSecret, secret);
        }

        return false;
    }

    private static string? SecretOf(string configJson)
    {
        try
        {
            return JsonSerializer.Deserialize<WebhookTriggerConfig>(configJson, JsonSerializerOptions.Web)?.Secret;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
}
