using AutomateX.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Xunit;

namespace AutomateX.Tests;

// Rules encoded ahead of the implementation:
// - no configured key = everything passes (dev/local default)
// - configured key gates /api and /hubs; /api/auth (the login endpoint) and other paths pass
// - key accepted via X-Api-Key header (API clients) or the HttpOnly session cookie
//   (browser — rides the websocket handshake too). Never via query string: URLs leak into logs.
// - wrong or missing key = 401 and the pipeline stops
public sealed class ApiKeyMiddlewareTests
{
    private static async Task<(int StatusCode, bool NextCalled)> RunAsync(
        string? configuredKey,
        string path,
        string? headerKey = null,
        string? cookieKey = null)
    {
        var nextCalled = false;
        var middleware = new ApiKeyMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            Options.Create(new ApiKeyOptions { ApiKey = configuredKey }));

        var context = new DefaultHttpContext();
        context.Request.Path = path;
        if (headerKey is not null)
        {
            context.Request.Headers["X-Api-Key"] = headerKey;
        }

        if (cookieKey is not null)
        {
            context.Request.Headers.Cookie = $"{ApiKeyMiddleware.CookieName}={cookieKey}";
        }

        await middleware.InvokeAsync(context);
        return (context.Response.StatusCode, nextCalled);
    }

    [Fact]
    public async Task No_configured_key_allows_everything()
    {
        var (status, nextCalled) = await RunAsync(configuredKey: null, "/api/workflows");

        Assert.True(nextCalled);
        Assert.Equal(StatusCodes.Status200OK, status);
    }

    [Fact]
    public async Task Missing_key_is_401_on_protected_paths()
    {
        var (status, nextCalled) = await RunAsync("secret", "/api/workflows");

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status401Unauthorized, status);
    }

    [Fact]
    public async Task Header_key_passes()
    {
        var (status, nextCalled) = await RunAsync("secret", "/api/workflows", headerKey: "secret");

        Assert.True(nextCalled);
        Assert.Equal(StatusCodes.Status200OK, status);
    }

    [Fact]
    public async Task Cookie_key_passes_including_websocket_paths()
    {
        var (status, nextCalled) = await RunAsync("secret", "/hubs/executions", cookieKey: "secret");

        Assert.True(nextCalled);
        Assert.Equal(StatusCodes.Status200OK, status);
    }

    [Fact]
    public async Task Auth_endpoints_are_not_gated()
    {
        var (status, nextCalled) = await RunAsync("secret", "/api/auth/session");

        Assert.True(nextCalled);
        Assert.Equal(StatusCodes.Status200OK, status);
    }

    [Fact]
    public async Task Webhook_paths_are_not_gated_by_the_api_key()
    {
        // Rule change (v1.3): webhooks authenticate with per-trigger secrets instead —
        // third-party senders can't set the X-Api-Key header and must never hold the global key.
        var (status, nextCalled) = await RunAsync("secret", "/api/webhooks/some-trigger-id");

        Assert.True(nextCalled);
        Assert.Equal(StatusCodes.Status200OK, status);
    }

    [Fact]
    public async Task Wrong_key_is_401()
    {
        var (status, nextCalled) = await RunAsync("secret", "/api/workflows", headerKey: "nope");

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status401Unauthorized, status);
    }

    [Fact]
    public async Task Unprotected_paths_pass_without_key()
    {
        var (status, nextCalled) = await RunAsync("secret", "/health");

        Assert.True(nextCalled);
        Assert.Equal(StatusCodes.Status200OK, status);
    }
}
