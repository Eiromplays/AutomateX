using System.Security.Claims;
using AutomateX.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Xunit;

namespace AutomateX.Tests;

// Auth gate rules (v4.6 — no static key). Tri-state collapses to: open (nothing configured), or engaged
// (OIDC or any DB key). This suite covers the config-only paths (open + OIDC + exemptions); the DB-key
// resolution + presence-engagement paths live in ScopedApiKeyGateTests (they need a real resolver).
// - nothing configured = everything passes (dev/local default)
// - OIDC mode: an authenticated cookie principal passes; anything else is 401
// - /api/auth (login), /api/webhooks (per-trigger secrets) and /api/version are exempt
public sealed class AuthGateMiddlewareTests
{
    private static async Task<(int StatusCode, bool NextCalled)> RunAsync(
        AuthOptions options, string path, bool authenticatedUser = false)
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
        if (authenticatedUser)
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity("test"));
        }

        await middleware.InvokeAsync(context);
        return (context.Response.StatusCode, nextCalled);
    }

    private static AuthOptions OidcOnly => new() { Authority = "https://login.example.com/v2.0", ClientId = "client" };

    [Fact]
    public async Task Nothing_configured_allows_everything()
    {
        var (status, nextCalled) = await RunAsync(new AuthOptions(), "/api/workflows");

        Assert.True(nextCalled);
        Assert.Equal(StatusCodes.Status200OK, status);
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
    public async Task Auth_endpoints_are_not_gated()
    {
        var (status, nextCalled) = await RunAsync(OidcOnly, "/api/auth/session");

        Assert.True(nextCalled);
        Assert.Equal(StatusCodes.Status200OK, status);
    }

    [Fact]
    public async Task Webhook_paths_are_not_gated()
    {
        // Webhooks authenticate with per-trigger secrets in any mode.
        var (status, nextCalled) = await RunAsync(OidcOnly, "/api/webhooks/some-trigger-id");

        Assert.True(nextCalled);
        Assert.Equal(StatusCodes.Status200OK, status);
    }

    [Fact]
    public async Task Version_endpoint_is_not_gated()
    {
        var (status, nextCalled) = await RunAsync(OidcOnly, "/api/version");

        Assert.True(nextCalled);
        Assert.Equal(StatusCodes.Status200OK, status);
    }

    [Fact]
    public async Task Unprotected_paths_pass_without_credentials()
    {
        var (status, nextCalled) = await RunAsync(OidcOnly, "/health");

        Assert.True(nextCalled);
        Assert.Equal(StatusCodes.Status200OK, status);
    }
}
