using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;

namespace AutomateX.Web;

// Silent refresh of the OIDC session. With offline_access + SaveTokens, the access
// token (and its expiry) ride in the auth cookie; OnValidatePrincipal checks it on
// each request and, when it is at/near expiry, trades the refresh token for a fresh
// set at the IdP. A failed refresh (revoked/disabled user, rotated-away refresh
// token) ends the session — so cookie liveness tracks the IdP, not a blind 8h slide.
//
// The decision/parse/apply logic is pure and unit-tested; the handler is thin glue
// over IOidcTokenClient so the reject-vs-renew flow is testable without a live IdP.

public sealed record RefreshedTokens(string AccessToken, string? RefreshToken, string? IdToken, DateTimeOffset ExpiresAt);

public static class OidcTokenRefresh
{
    public static bool ShouldRefresh(string? refreshToken, string? expiresAtRaw, DateTimeOffset now, TimeSpan leeway)
    {
        if (string.IsNullOrEmpty(refreshToken))
        {
            return false;
        }

        // expires_at is stored by SaveTokens as a round-trippable UTC string; no/garbage
        // value means we have no basis to act, so leave the cookie's own expiry in charge.
        return DateTimeOffset.TryParse(
                   expiresAtRaw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var expiresAt)
               && now >= expiresAt - leeway;
    }

    public static RefreshedTokens? ParseTokenResponse(string json, DateTimeOffset now)
    {
        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(json);
            root = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }

        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("access_token", out var accessToken)
            || accessToken.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var expiresAt = root.TryGetProperty("expires_in", out var expiresIn)
                        && expiresIn.ValueKind == JsonValueKind.Number
                        && expiresIn.TryGetInt32(out var seconds)
            ? now.AddSeconds(seconds)
            : now.AddMinutes(5);

        string? StringOrNull(string name) =>
            root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

        return new RefreshedTokens(accessToken.GetString()!, StringOrNull("refresh_token"), StringOrNull("id_token"), expiresAt);
    }

    public static void Apply(AuthenticationProperties properties, RefreshedTokens tokens)
    {
        // Rebuild the stored set so absent tokens (e.g. a first id_token) are added,
        // not silently dropped the way UpdateTokenValue would. A null rotation field
        // leaves the existing value in place.
        var stored = properties.GetTokens().ToDictionary(token => token.Name, token => token.Value);
        stored["access_token"] = tokens.AccessToken;
        if (tokens.RefreshToken is not null)
        {
            stored["refresh_token"] = tokens.RefreshToken;
        }

        if (tokens.IdToken is not null)
        {
            stored["id_token"] = tokens.IdToken;
        }

        stored["expires_at"] = tokens.ExpiresAt.ToString("o", CultureInfo.InvariantCulture);
        properties.StoreTokens(stored.Select(token => new AuthenticationToken { Name = token.Key, Value = token.Value }));
    }
}

// Exchanges a refresh token at the provider's token endpoint. Returns the raw 2xx
// body (parsed by OidcTokenRefresh), or null on any non-success — the untested glue.
public interface IOidcTokenClient
{
    Task<string?> RefreshAsync(string refreshToken, CancellationToken cancellationToken);
}

public sealed class OidcTokenClient(
    IHttpClientFactory httpClientFactory,
    IOptionsMonitor<OpenIdConnectOptions> oidcOptions) : IOidcTokenClient
{
    public async Task<string?> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var options = oidcOptions.Get(OpenIdConnectDefaults.AuthenticationScheme);
        var configuration = options.Configuration
            ?? await options.ConfigurationManager!.GetConfigurationAsync(cancellationToken);

        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = options.ClientId ?? string.Empty,
            ["client_secret"] = options.ClientSecret ?? string.Empty,
        });

        var http = httpClientFactory.CreateClient();
        using var response = await http.PostAsync(configuration.TokenEndpoint, content, cancellationToken);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadAsStringAsync(cancellationToken)
            : null;
    }
}

public sealed class CookieTokenRefresher(IOidcTokenClient tokenClient, ILogger<CookieTokenRefresher> logger)
{
    // Refresh slightly ahead of expiry so an in-flight request never races the boundary.
    public static readonly TimeSpan Leeway = TimeSpan.FromMinutes(2);

    public async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        var properties = context.Properties;
        var refreshToken = properties.GetTokenValue("refresh_token");

        if (!OidcTokenRefresh.ShouldRefresh(refreshToken, properties.GetTokenValue("expires_at"), DateTimeOffset.UtcNow, Leeway))
        {
            return;
        }

        var body = await tokenClient.RefreshAsync(refreshToken!, context.HttpContext.RequestAborted);
        var refreshed = body is null ? null : OidcTokenRefresh.ParseTokenResponse(body, DateTimeOffset.UtcNow);
        if (refreshed is null)
        {
            logger.LogInformation("OIDC token refresh failed; ending session");
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return;
        }

        OidcTokenRefresh.Apply(properties, refreshed);
        context.ShouldRenew = true;
    }
}
