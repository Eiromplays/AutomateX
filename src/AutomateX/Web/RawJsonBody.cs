using System.Text.Json;

namespace AutomateX.Web;

internal static class RawJsonBody
{
    // Returns the raw request body when it is non-empty valid JSON, null when empty.
    // Throws JsonException on invalid JSON — callers translate to a 400.
    public static async Task<string?> ReadAsync(HttpContext context, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(context.Request.Body);
        var body = await reader.ReadToEndAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        using var _ = JsonDocument.Parse(body);
        return body;
    }
}
