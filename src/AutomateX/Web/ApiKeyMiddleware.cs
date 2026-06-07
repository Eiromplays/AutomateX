using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace AutomateX.Web;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    // Machine-client credential (X-Api-Key header) and the no-IdP browser fallback
    // (exchanged for an HttpOnly cookie via POST /api/auth/session).
    public string? ApiKey { get; set; }

    // OIDC (Entra ID or any compliant provider). When set, browsers authenticate via
    // the OpenIdConnect challenge + auth cookie; the API key keeps working for scripts.
    // e.g. https://login.microsoftonline.com/<tenant-id>/v2.0
    public string? Authority { get; set; }

    public string? ClientId { get; set; }

    public string? ClientSecret { get; set; }

    public bool OidcConfigured => !string.IsNullOrEmpty(Authority) && !string.IsNullOrEmpty(ClientId);
}

// Tri-state gate for /api and /hubs: open (nothing configured), API key, or OIDC —
// where an authenticated cookie principal passes and the API key remains valid for
// machine clients. /api/auth (login) and /api/webhooks (per-trigger secrets) are exempt.
public sealed class AuthGateMiddleware(RequestDelegate next, IOptions<AuthOptions> options)
{
    public const string CookieName = "automatex-key";

    public async Task InvokeAsync(HttpContext context)
    {
        var auth = options.Value;
        var hasKey = !string.IsNullOrEmpty(auth.ApiKey);

        if ((!hasKey && !auth.OidcConfigured) || !IsProtected(context.Request.Path))
        {
            await next(context);
            return;
        }

        if (hasKey)
        {
            var providedKey = context.Request.Headers["X-Api-Key"].FirstOrDefault()
                ?? context.Request.Cookies[CookieName];

            if (providedKey is not null && FixedTimeEquals(providedKey, auth.ApiKey!))
            {
                await next(context);
                return;
            }
        }

        if (auth.OidcConfigured && context.User.Identity?.IsAuthenticated == true)
        {
            await next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsync("Authentication required.");
    }

    private static bool IsProtected(PathString path) =>
        (path.StartsWithSegments("/api")
            && !path.StartsWithSegments("/api/auth")
            && !path.StartsWithSegments("/api/webhooks"))
        || path.StartsWithSegments("/hubs");

    internal static bool FixedTimeEquals(string provided, string configured) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(provided),
            Encoding.UTF8.GetBytes(configured));
}
