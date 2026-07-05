using AutomateX.Modules.ApiKeys;
using Microsoft.Extensions.Options;

namespace AutomateX.Web;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    // Opt-in no-IdP first run: when true and OIDC is not configured, startup mints a single Instance
    // admin key (if none exist) and logs it once. Grants nothing itself — see ApiKeyBootstrap.
    public bool Bootstrap { get; set; }

    // OIDC (Entra ID or any compliant provider). When set, browsers authenticate via
    // the OpenIdConnect challenge + auth cookie. Dynamic API keys (atx_…) work alongside it.
    // e.g. https://login.microsoftonline.com/<tenant-id>/v2.0
    public string? Authority { get; set; }

    public string? ClientId { get; set; }

    public string? ClientSecret { get; set; }

    // Auth cookie lifetime (sliding). Override via Auth__SessionLifetime, e.g.
    // "7.00:00:00" (7 days) or "08:00:00" (8 hours); unset = the framework default.
    public TimeSpan? SessionLifetime { get; set; }

    // Instance operators (a role above workspace-owner): OIDC subjects and/or emails, e.g.
    // Auth__InstanceAdmins__0=admin@corp.com. Only consulted in OIDC mode — open/api-key callers
    // through the gate are already operators. An admin grant is config-only, never inferred from data.
    public List<string> InstanceAdmins { get; set; } = [];

    public bool OidcConfigured => !string.IsNullOrEmpty(Authority) && !string.IsNullOrEmpty(ClientId);

    public TimeSpan ResolvedSessionLifetime =>
        SessionLifetime is { } value && value > TimeSpan.Zero ? value : TimeSpan.FromDays(14);
}

// Gate for /api and /hubs. Order for a protected path once the instance is engaged:
//   1. a presented atx_ token (X-Api-Key header or the HttpOnly cookie) → ApiKeyResolver → on hit set
//      ApiKeyContext + pass, on miss 401;
//   2. an authenticated OIDC cookie principal;
//   else 401.
// The instance stays fully open (nothing gated) until it has a credential — OIDC configured or any DB
// key — so local dev keeps working. /api/auth (login) and /api/webhooks (per-trigger secrets) are exempt.
public sealed class AuthGateMiddleware(RequestDelegate next, IOptions<AuthOptions> options)
{
    public const string CookieName = "automatex-key";

    public async Task InvokeAsync(HttpContext context)
    {
        if (!IsProtected(context.Request.Path))
        {
            await next(context);
            return;
        }

        var auth = options.Value;
        var resolver = context.RequestServices?.GetService<ApiKeyResolver>();

        var engaged = auth.OidcConfigured
            || (resolver is not null && await resolver.HasActiveKeysAsync(context.RequestAborted));
        if (!engaged)
        {
            await next(context);
            return;
        }

        var presented = context.Request.Headers["X-Api-Key"].FirstOrDefault()
            ?? context.Request.Cookies[CookieName];

        // A presented atx_ token is a credential claim: resolve it or reject — never fall back to OIDC.
        if (presented is not null && ApiKeyToken.LooksLikeToken(presented) && resolver is not null)
        {
            if (await resolver.ResolveAsync(presented, context.RequestAborted) is { } apiKey)
            {
                ApiKeyContext.Set(context, apiKey);
                await next(context);
                return;
            }
        }
        else if (auth.OidcConfigured && context.User.Identity?.IsAuthenticated == true)
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
            && !path.StartsWithSegments("/api/webhooks")
            && !path.StartsWithSegments("/api/version"))
        || path.StartsWithSegments("/hubs");
}
