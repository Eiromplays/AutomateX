using System.Text.Json;

namespace AutomateX.Web;

internal static class RawJsonBody
{
    // Returns the raw request body when it is non-empty valid JSON, null when empty.
    // Throws JsonException on invalid JSON — callers translate to a 400.
    public static async Task<string?> ReadAsync(HttpContext context, CancellationToken cancellationToken)
    {
        var body = await ReadRawAsync(context, cancellationToken);
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        using var _ = JsonDocument.Parse(body);
        return body;
    }

    // The exact raw body, unparsed (empty string when none). Used where the bytes matter before
    // JSON validation — e.g. verifying a webhook HMAC signature over what was actually sent.
    public static async Task<string> ReadRawAsync(HttpContext context, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(context.Request.Body);
        return await reader.ReadToEndAsync(cancellationToken);
    }
}
