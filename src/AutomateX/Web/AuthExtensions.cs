using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace AutomateX.Web;

public sealed record AuthMe(string Mode, bool Authenticated, string? Name, string? Email);

// API-side OIDC (recorded decision): the proxy makes browser↔API same-origin, so the
// API owns the OIDC flow with standard ASP.NET middleware — no tokens in the browser,
// no SSR/BFF needed. Falls back to API-key or open mode when OIDC is not configured.
public static class AuthExtensions
{
    public static WebApplicationBuilder AddAutomateXAuth(this WebApplicationBuilder builder)
    {
        builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection(AuthOptions.SectionName));

        var auth = builder.Configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions();
        if (!auth.OidcConfigured)
        {
            return builder;
        }

        builder.Services.AddAuthentication(options =>
            {
                options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
            })
            .AddCookie(options =>
            {
                options.Cookie.Name = "automatex-auth";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.ExpireTimeSpan = TimeSpan.FromHours(8);
                options.SlidingExpiration = true;
                // API/XHR callers get a 401 to handle, not a redirect to chase.
                options.Events.OnRedirectToLogin = context =>
                {
                    if (context.Request.Path.StartsWithSegments("/api"))
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        return Task.CompletedTask;
                    }

                    context.Response.Redirect(context.RedirectUri);
                    return Task.CompletedTask;
                };
            })
            .AddOpenIdConnect(options =>
            {
                options.Authority = auth.Authority;
                options.ClientId = auth.ClientId;
                options.ClientSecret = auth.ClientSecret;
                options.ResponseType = OpenIdConnectResponseType.Code;
                options.Scope.Clear();
                options.Scope.Add("openid");
                options.Scope.Add("profile");
                options.Scope.Add("email");
                options.SaveTokens = false;
                options.TokenValidationParameters.NameClaimType = "name";
            });

        builder.Services.AddAuthorization();

        // Behind Caddy/Vite the OIDC redirect URI must be built from forwarded headers.
        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
        });

        return builder;
    }

    public static WebApplication UseAutomateXAuth(this WebApplication app)
    {
        var auth = app.Services.GetRequiredService<IOptions<AuthOptions>>().Value;

        if (auth.OidcConfigured)
        {
            app.UseForwardedHeaders();
            app.UseAuthentication();

            app.MapGet("/auth/login", (string? returnUrl) =>
                Results.Challenge(
                    new AuthenticationProperties { RedirectUri = SafeLocalUrl(returnUrl) },
                    [OpenIdConnectDefaults.AuthenticationScheme]));

            app.MapGet("/auth/logout", () =>
                Results.SignOut(
                    new AuthenticationProperties { RedirectUri = "/" },
                    [CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme]));
        }

        app.UseMiddleware<AuthGateMiddleware>();

        app.MapGet("/api/auth/me", (HttpContext context, IOptions<AuthOptions> options) =>
        {
            var current = options.Value;
            if (current.OidcConfigured)
            {
                var user = context.User;
                return Results.Ok(new AuthMe(
                    "oidc",
                    user.Identity?.IsAuthenticated == true,
                    user.FindFirst("name")?.Value ?? user.Identity?.Name,
                    user.FindFirst("preferred_username")?.Value));
            }

            if (!string.IsNullOrEmpty(current.ApiKey))
            {
                var cookie = context.Request.Cookies[AuthGateMiddleware.CookieName];
                var authenticated = cookie is not null && AuthGateMiddleware.FixedTimeEquals(cookie, current.ApiKey);
                return Results.Ok(new AuthMe("apikey", authenticated, null, null));
            }

            return Results.Ok(new AuthMe("open", true, null, null));
        });

        return app;
    }

    private static string SafeLocalUrl(string? returnUrl) =>
        returnUrl is ['/', ..] && !returnUrl.StartsWith("//", StringComparison.Ordinal) ? returnUrl : "/";
}
