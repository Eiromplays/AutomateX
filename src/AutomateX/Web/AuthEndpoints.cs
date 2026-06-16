using FastEndpoints;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace AutomateX.Web;

// Exchanges the API key for an HttpOnly, SameSite=Strict session cookie so the
// SPA never holds the key in JS-readable storage and websockets authenticate
// via the handshake's cookies instead of query strings.
public static class CreateSession
{
    public sealed class Endpoint(IOptions<AuthOptions> options) : Endpoint<Request>
    {
        public override void Configure()
        {
            Post("auth/session");
            AllowAnonymous();
            Options(b => b.RequireRateLimiting(RateLimitPolicies.Auth));
        }

        public override async Task HandleAsync(Request req, CancellationToken ct)
        {
            var configuredKey = options.Value.ApiKey;

            if (!string.IsNullOrEmpty(configuredKey))
            {
                if (string.IsNullOrEmpty(req.Key) || !AuthGateMiddleware.FixedTimeEquals(req.Key, configuredKey))
                {
                    await Send.UnauthorizedAsync(ct);
                    return;
                }

                HttpContext.Response.Cookies.Append(AuthGateMiddleware.CookieName, req.Key, new CookieOptions
                {
                    HttpOnly = true,
                    SameSite = SameSiteMode.Strict,
                    Secure = HttpContext.Request.IsHttps,
                    MaxAge = TimeSpan.FromDays(30),
                    Path = "/",
                });
            }

            await Send.NoContentAsync(ct);
        }
    }

    public sealed record Request(string Key);
}

public static class DeleteSession
{
    public sealed class Endpoint : EndpointWithoutRequest
    {
        public override void Configure()
        {
            Delete("auth/session");
            AllowAnonymous();
        }

        public override async Task HandleAsync(CancellationToken ct)
        {
            HttpContext.Response.Cookies.Delete(AuthGateMiddleware.CookieName, new CookieOptions { Path = "/" });
            await Send.NoContentAsync(ct);
        }
    }
}
