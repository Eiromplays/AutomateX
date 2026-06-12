using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AutomateX.Plugin.Sdk;

namespace AutomateX.Engine.Connections;

// The tokens obtained from a code exchange or refresh. RefreshToken/ExpiresAt may be absent
// (some providers omit a refresh token on refresh, or issue non-expiring tokens).
public sealed record OAuthTokens(string AccessToken, string? RefreshToken, DateTimeOffset? ExpiresAt);

// PKCE (RFC 7636) — a high-entropy verifier and its S256 challenge, base64url without padding.
public static class Pkce
{
    public static string NewVerifier() => Base64Url(RandomNumberGenerator.GetBytes(32));

    public static string Challenge(string verifier) =>
        Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

// Pure OAuth2 authorization-code helpers — URL building, token-response parsing and the
// refresh decision. The HTTP calls and persistence live in the flow service that uses these.
public static class OAuthFlow
{
    public static string BuildAuthorizeUrl(OAuthConfig config, string redirectUri, string state, string? codeChallenge)
    {
        var query = new List<string>
        {
            "response_type=code",
            "client_id=" + Uri.EscapeDataString(config.ClientId),
            "redirect_uri=" + Uri.EscapeDataString(redirectUri),
            "state=" + Uri.EscapeDataString(state),
        };

        if (config.Scopes.Count > 0)
        {
            query.Add("scope=" + Uri.EscapeDataString(string.Join(' ', config.Scopes)));
        }

        if (codeChallenge is not null)
        {
            query.Add("code_challenge=" + Uri.EscapeDataString(codeChallenge));
            query.Add("code_challenge_method=S256");
        }

        var separator = config.AuthorizationEndpoint.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        return config.AuthorizationEndpoint + separator + string.Join('&', query);
    }

    // A token endpoint response. `expires_in` (seconds) is turned into an absolute instant so
    // the refresh decision doesn't depend on when the row is later read.
    public static OAuthTokens ParseTokenResponse(string json, DateTimeOffset now)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("access_token", out var accessToken) || accessToken.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException("Token response had no access_token.");
        }

        var refreshToken = root.TryGetProperty("refresh_token", out var refresh) && refresh.ValueKind == JsonValueKind.String
            ? refresh.GetString()
            : null;

        // expires_in is seconds; providers send it as a number or (occasionally) a string.
        DateTimeOffset? expiresAt = null;
        if (root.TryGetProperty("expires_in", out var expiresIn))
        {
            long? seconds = expiresIn.ValueKind switch
            {
                JsonValueKind.Number when expiresIn.TryGetInt64(out var n) => n,
                JsonValueKind.String when long.TryParse(expiresIn.GetString(), out var n) => n,
                _ => null,
            };
            if (seconds is { } value)
            {
                expiresAt = now.AddSeconds(value);
            }
        }

        return new OAuthTokens(accessToken.GetString()!, refreshToken, expiresAt);
    }

    // Refresh when the token is at or past (expiry − skew). Unknown expiry → never proactively
    // refresh (the provider issued a non-expiring token, or didn't say).
    public static bool NeedsRefresh(DateTimeOffset? expiresAt, DateTimeOffset now, TimeSpan skew) =>
        expiresAt is { } expiry && now >= expiry - skew;
}
