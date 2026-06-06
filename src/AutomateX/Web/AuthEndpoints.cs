using FastEndpoints;
using Microsoft.Extensions.Options;

namespace AutomateX.Web;

// Exchanges the API key for an HttpOnly, SameSite=Strict session cookie so the
// SPA never holds the key in JS-readable storage and websockets authenticate
// via the handshake's cookies instead of query strings.
public static class CreateSession
{
    public sealed class Endpoint(IOptions<ApiKeyOptions> options) : Endpoint<Request>
    {
        public override void Configure()
        {
            Post("auth/session");
            AllowAnonymous();
        }

        public override async Task HandleAsync(Request req, CancellationToken ct)
        {
            var configuredKey = options.Value.ApiKey;

            if (!string.IsNullOrEmpty(configuredKey))
            {
                if (string.IsNullOrEmpty(req.Key) || !ApiKeyMiddleware.FixedTimeEquals(req.Key, configuredKey))
                {
                    await Send.UnauthorizedAsync(ct);
                    return;
                }

                HttpContext.Response.Cookies.Append(ApiKeyMiddleware.CookieName, req.Key, new CookieOptions
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
            HttpContext.Response.Cookies.Delete(ApiKeyMiddleware.CookieName, new CookieOptions { Path = "/" });
            await Send.NoContentAsync(ct);
        }
    }
}
