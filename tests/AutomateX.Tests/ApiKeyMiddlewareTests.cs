using System.Security.Claims;
using AutomateX.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Xunit;

namespace AutomateX.Tests;

// Auth gate rules (tri-state — open / api key / OIDC):
// - nothing configured = everything passes (dev/local default)
// - configured gate covers /api and /hubs; /api/auth (login) and /api/webhooks
//   (per-trigger secrets) and other paths pass
// - API-key mode: X-Api-Key header (machine clients) or the HttpOnly session cookie
// - OIDC mode: an authenticated principal (cookie scheme) passes; the X-Api-Key header
//   still passes when a key is ALSO configured (scripts keep working alongside OIDC)
// - anything else = 401 and the pipeline stops
public sealed class AuthGateMiddlewareTests
{
    private static async Task<(int StatusCode, bool NextCalled)> RunAsync(
        AuthOptions options,
        string path,
        string? headerKey = null,
        string? cookieKey = null,
        bool authenticatedUser = false)
    {
        var nextCalled = false;
        var middleware = new AuthGateMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            Options.Create(options));

        var context = new DefaultHttpContext();
        context.Request.Path = path;
        if (headerKey is not null)
        {
            context.Request.Headers["X-Api-Key"] = headerKey;
        }

        if (cookieKey is not null)
        {
            context.Request.Headers.Cookie = $"{AuthGateMiddleware.CookieName}={cookieKey}";
        }

        if (authenticatedUser)
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity("test"));
        }

        await middleware.InvokeAsync(context);
        return (context.Response.StatusCode, nextCalled);
    }

    private static AuthOptions KeyOnly => new() { ApiKey = "secret" };

    private static AuthOptions OidcOnly => new() { Authority = "https://login.example.com/v2.0", ClientId = "client" };

    private static AuthOptions OidcAndKey => new()
    {
        Authority = "https://login.example.com/v2.0",
        ClientId = "client",
        ApiKey = "secret",
    };

    [Fact]
    public async Task Nothing_configured_allows_everything()
    {
        var (status, nextCalled) = await RunAsync(new AuthOptions(), "/api/workflows");

        Assert.True(nextCalled);
        Assert.Equal(StatusCodes.Status200OK, status);
    }

    [Fact]
    public async Task ApiKey_mode_missing_key_is_401()
    {
        var (status, nextCalled) = await RunAsync(KeyOnly, "/api/workflows");

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status401Unauthorized, status);
    }

    [Fact]
    public async Task ApiKey_mode_header_key_passes()
    {
        var (status, nextCalled) = await RunAsync(KeyOnly, "/api/workflows", headerKey: "secret");

        Assert.True(nextCalled);
        Assert.Equal(StatusCodes.Status200OK, status);
    }

    [Fact]
    public async Task ApiKey_mode_cookie_passes_including_websocket_paths()
    {
        var (status, nextCalled) = await RunAsync(KeyOnly, "/hubs/executions", cookieKey: "secret");

        Assert.True(nextCalled);
        Assert.Equal(StatusCodes.Status200OK, status);
    }

    [Fact]
    public async Task ApiKey_mode_wrong_key_is_401()
    {
        var (status, nextCalled) = await RunAsync(KeyOnly, "/api/workflows", headerKey: "nope");

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status401Unauthorized, status);
    }

    [Fact]
    public async Task Oidc_mode_authenticated_user_passes()
    {
        var (status, nextCalled) = await RunAsync(OidcOnly, "/api/workflows", authenticatedUser: true);

        Assert.True(nextCalled);
        Assert.Equal(StatusCodes.Status200OK, status);
    }

    [Fact]
    public async Task Oidc_mode_unauthenticated_is_401()
    {
        var (status, nextCalled) = await RunAsync(OidcOnly, "/api/workflows");

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status401Unauthorized, status);
    }

    [Fact]
    public async Task Oidc_mode_api_key_header_still_passes_for_machine_clients()
    {
        var (status, nextCalled) = await RunAsync(OidcAndKey, "/api/workflows", headerKey: "secret");

        Assert.True(nextCalled);
        Assert.Equal(StatusCodes.Status200OK, status);
    }

    [Fact]
    public async Task Auth_endpoints_are_not_gated()
    {
        var (status, nextCalled) = await RunAsync(KeyOnly, "/api/auth/session");

        Assert.True(nextCalled);
        Assert.Equal(StatusCodes.Status200OK, status);
    }

    [Fact]
    public async Task Webhook_paths_are_not_gated()
    {
        // Webhooks authenticate with per-trigger secrets in any mode.
        var (status, nextCalled) = await RunAsync(OidcAndKey, "/api/webhooks/some-trigger-id");

        Assert.True(nextCalled);
        Assert.Equal(StatusCodes.Status200OK, status);
    }

    [Fact]
    public async Task Unprotected_paths_pass_without_credentials()
    {
        var (status, nextCalled) = await RunAsync(OidcAndKey, "/health");

        Assert.True(nextCalled);
        Assert.Equal(StatusCodes.Status200OK, status);
    }
}
