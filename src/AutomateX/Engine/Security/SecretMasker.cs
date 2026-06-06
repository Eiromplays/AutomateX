using System.Text.Json;

namespace AutomateX.Engine.Security;

// GitHub-Actions-style masking: known secret values are replaced with *** in anything
// persisted or published (step outputs, errors, events). Best-effort by definition —
// transformed secrets (base64, substrings) can't be recognized.
public static class SecretMasker
{
    public const string Mask = "***";

    public static string? MaskSecrets(string? text, IReadOnlyCollection<string> secrets)
    {
        if (string.IsNullOrEmpty(text) || secrets.Count == 0)
        {
            return text;
        }

        var result = text;
        foreach (var secret in secrets)
        {
            if (string.IsNullOrEmpty(secret))
            {
                continue;
            }

            result = result.Replace(secret, Mask, StringComparison.Ordinal);

            // Outputs are serialized JSON — mask the escaped form when it differs.
            var escaped = JsonEncodedText.Encode(secret).ToString();
            if (escaped != secret)
            {
                result = result.Replace(escaped, Mask, StringComparison.Ordinal);
            }
        }

        return result;
    }
}
