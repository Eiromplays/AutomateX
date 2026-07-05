using AutomateX.Modules.ApiKeys;
using FastEndpoints;
using Microsoft.AspNetCore.RateLimiting;

namespace AutomateX.Web;

// Exchanges a valid API key for an HttpOnly, SameSite=Strict session cookie so the SPA never holds the
// key in JS-readable storage and websockets authenticate via the handshake's cookies. The gate resolves
// the cookie on every request, so revocation/expiry take effect immediately regardless of cookie age.
public static class CreateSession
{
    public sealed class Endpoint(ApiKeyResolver resolver) : Endpoint<Request>
    {
        public override void Configure()
        {
            Post("auth/session");
            AllowAnonymous();
            Options(b => b.RequireRateLimiting(RateLimitPolicies.Auth));
        }

        public override async Task HandleAsync(Request req, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(req.Key) || !ApiKeyToken.LooksLikeToken(req.Key)
                || await resolver.ResolveAsync(req.Key, ct) is null)
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
