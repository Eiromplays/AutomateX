using System.Globalization;
using System.Text.Json;
using AutomateX.Database;
using AutomateX.Engine.Connections;
using AutomateX.Engine.Security;
using AutomateX.Modules.Workspaces;
using AutomateX.Plugin.Sdk;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AutomateX.Modules.Connections.Features;

// Step 1: an authenticated call that returns the provider's consent URL; the SPA then sends
// the browser there. State (connection + workspace + PKCE verifier) is encrypted into the
// `state` param so the callback needs no header or server-side session.
public static class StartOAuthConnect
{
    public sealed class Endpoint(
        AutomateXDbContext dbContext,
        TenantCipher cipher,
        ConnectionTypeRegistry registry,
        OAuthStateProtector stateProtector,
        WorkspaceAccess access) : EndpointWithoutRequest<Response>
    {
        public override void Configure()
        {
            Post("connections/{id}/oauth/start");
            AllowAnonymous();
        }

        public override async Task HandleAsync(CancellationToken ct)
        {
            if (await access.AuthorizeAsync(HttpContext, WorkspaceRole.Editor, ct) is not { } ws)
            {
                await Send.ForbiddenAsync(ct);
                return;
            }

            var id = Route<Guid>("id");
            var connection = await dbContext.Connections
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == id && x.WorkspaceId == ws, ct);

            if (connection is null)
            {
                await Send.NotFoundAsync(ct);
                return;
            }

            if (connection.Provider is null || registry.GetInstance(connection.Provider) is not IOAuthConnectionType oauthType)
            {
                ThrowError("This connection type does not support OAuth.");
                return;
            }

            Dictionary<string, string> values;
            try
            {
                values = JsonSerializer.Deserialize<Dictionary<string, string>>(
                    await cipher.DecryptAsync(connection.EncryptedSecrets, connection.WorkspaceId, ct)) ?? [];
            }
            catch (SecretCipherException ex)
            {
                ThrowError(ex.Message);
                return;
            }

            var config = oauthType.BuildOAuthConfig(values);
            if (string.IsNullOrWhiteSpace(config.AuthorizationEndpoint)
                || string.IsNullOrWhiteSpace(config.TokenEndpoint)
                || string.IsNullOrWhiteSpace(config.ClientId))
            {
                ThrowError("The connection is missing its authorization endpoint, token endpoint or client id.");
                return;
            }

            var verifier = config.UsePkce ? Pkce.NewVerifier() : null;
            var state = stateProtector.Protect(
                new OAuthStateData(connection.Id, ws, verifier ?? "", DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
            var authorizeUrl = OAuthFlow.BuildAuthorizeUrl(
                config, RedirectUri(HttpContext), state, verifier is null ? null : Pkce.Challenge(verifier));

            await Send.OkAsync(new Response(authorizeUrl), ct);
        }
    }

    // Must match what the user registered with the provider and what the token exchange sends.
    internal static string RedirectUri(HttpContext context) =>
        $"{context.Request.Scheme}://{context.Request.Host}/api/connections/oauth/callback";

    public sealed record Response(string AuthorizeUrl);
}

// Step 2: the provider redirects the browser here. Recover state, exchange the code for tokens,
// and write them into the connection's encrypted blob. Always lands back on the SPA's
// /connections page with an outcome query.
public static class OAuthCallback
{
    public sealed class Endpoint(
        AutomateXDbContext dbContext,
        TenantCipher cipher,
        ConnectionTypeRegistry registry,
        OAuthStateProtector stateProtector,
        OAuthClient client,
        WorkspaceAccess access) : EndpointWithoutRequest
    {
        public override void Configure()
        {
            Get("connections/oauth/callback");
            AllowAnonymous();
        }

        public override async Task HandleAsync(CancellationToken ct)
        {
            var error = Query<string?>("error", isRequired: false);
            if (!string.IsNullOrEmpty(error))
            {
                await Back($"oauth_error={Uri.EscapeDataString(error)}");
                return;
            }

            var code = Query<string?>("code", isRequired: false);
            var state = Query<string?>("state", isRequired: false);
            if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
            {
                await Back("oauth_error=missing_code");
                return;
            }

            if (stateProtector.Unprotect(state) is not { } stateData)
            {
                await Back("oauth_error=invalid_state");
                return;
            }

            var role = await access.GetRoleAsync(stateData.WorkspaceId, HttpContext.User, ct);
            if (role is not { } resolved || resolved < WorkspaceRole.Editor)
            {
                await Send.ForbiddenAsync(ct);
                return;
            }

            var connection = await dbContext.Connections
                .FirstOrDefaultAsync(x => x.Id == stateData.ConnectionId && x.WorkspaceId == stateData.WorkspaceId, ct);
            if (connection is null
                || connection.Provider is null
                || registry.GetInstance(connection.Provider) is not IOAuthConnectionType oauthType)
            {
                await Back("oauth_error=connection_gone");
                return;
            }

            Dictionary<string, string> values;
            try
            {
                values = JsonSerializer.Deserialize<Dictionary<string, string>>(
                    await cipher.DecryptAsync(connection.EncryptedSecrets, connection.WorkspaceId, ct)) ?? [];
            }
            catch (SecretCipherException)
            {
                await Back("oauth_error=decrypt");
                return;
            }

            var config = oauthType.BuildOAuthConfig(values);
            OAuthTokens tokens;
            try
            {
                tokens = await client.ExchangeCodeAsync(
                    config,
                    StartOAuthConnect.RedirectUri(HttpContext),
                    code,
                    string.IsNullOrEmpty(stateData.Verifier) ? null : stateData.Verifier,
                    ct);
            }
            catch (OAuthException)
            {
                await Back("oauth_error=exchange_failed");
                return;
            }

            values["accessToken"] = tokens.AccessToken;
            if (tokens.RefreshToken is not null)
            {
                values["refreshToken"] = tokens.RefreshToken;
            }

            if (tokens.ExpiresAt is { } expiresAt)
            {
                values["expiresAt"] = expiresAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
            }

            connection.Update(null, await cipher.EncryptAsync(JsonSerializer.Serialize(values), connection.WorkspaceId, ct));
            await dbContext.SaveChangesAsync(ct);

            await Back("oauth=connected");
        }

        private Task Back(string query) => Send.RedirectAsync($"/connections?{query}", allowRemoteRedirects: false);
    }
}
