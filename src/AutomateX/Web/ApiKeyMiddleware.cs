using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace AutomateX.Web;

public sealed class ApiKeyOptions
{
    public const string SectionName = "Auth";

    // When set, /api and /hubs require it — X-Api-Key header for API clients, or the
    // HttpOnly session cookie for the browser (set via POST /api/auth/session).
    // Unset = open (local/dev). Proper OIDC (Entra ID) is the next auth pass.
    public string? ApiKey { get; set; }
}

public sealed class ApiKeyMiddleware(RequestDelegate next, IOptions<ApiKeyOptions> options)
{
    public const string CookieName = "automatex-key";

    public async Task InvokeAsync(HttpContext context)
    {
        var configuredKey = options.Value.ApiKey;
        if (string.IsNullOrEmpty(configuredKey) || !IsProtected(context.Request.Path))
        {
            await next(context);
            return;
        }

        var providedKey = context.Request.Headers["X-Api-Key"].FirstOrDefault()
            ?? context.Request.Cookies[CookieName];

        if (providedKey is not null && FixedTimeEquals(providedKey, configuredKey))
        {
            await next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsync("API key required.");
    }

    private static bool IsProtected(PathString path) =>
        (path.StartsWithSegments("/api")
            && !path.StartsWithSegments("/api/auth")
            // Webhooks carry per-trigger secrets — third-party senders never hold the global key.
            && !path.StartsWithSegments("/api/webhooks"))
        || path.StartsWithSegments("/hubs");

    internal static bool FixedTimeEquals(string provided, string configured) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(provided),
            Encoding.UTF8.GetBytes(configured));
}
