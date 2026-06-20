using System.Net;
using AutomateX.Database;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
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

        // Persist the key ring in Postgres so auth cookies survive restarts/recreation.
        // Always on (cheap, future-proof) — the table is read only when a DP-protected
        // payload like the OIDC auth cookie is actually used.
        builder.Services.AddDataProtection()
            .PersistKeysToDbContext<AutomateXDbContext>()
            .SetApplicationName("AutomateX");

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
                options.ExpireTimeSpan = auth.ResolvedSessionLifetime;
                options.SlidingExpiration = true;
                // Each request: refresh the OIDC tokens just before they expire (and
                // sign out if the IdP refuses), so the session tracks the provider.
                options.Events.OnValidatePrincipal = context =>
                    context.HttpContext.RequestServices
                        .GetRequiredService<CookieTokenRefresher>()
                        .ValidateAsync(context);
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
                // offline_access yields a refresh token; SaveTokens stows it (plus the
                // access token + expiry) in the cookie for OnValidatePrincipal to use.
                options.Scope.Add("offline_access");
                options.SaveTokens = true;
                options.TokenValidationParameters.NameClaimType = "name";
            });

        builder.Services.AddHttpClient();
        builder.Services.AddSingleton<IOidcTokenClient, OidcTokenClient>();
        builder.Services.AddSingleton<CookieTokenRefresher>();
        builder.Services.AddAuthorization();

        // Behind Caddy/Vite the OIDC redirect URI is built from forwarded Host/proto — but only
        // trust those headers from the proxy, never from arbitrary clients (spoofed Host/proto =
        // OIDC redirect/host poisoning). Default to private ranges (the proxy shares the container/
        // LAN network); pin tighter with ForwardedHeaders:KnownProxies (CSV of the proxy's IPs).
        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders =
                ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost | ForwardedHeaders.XForwardedFor;
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();

            var knownProxies = builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>();
            if (knownProxies is { Length: > 0 })
            {
                foreach (var proxy in knownProxies)
                {
                    if (IPAddress.TryParse(proxy, out var address))
                    {
                        options.KnownProxies.Add(address);
                    }
                }
            }
            else
            {
                foreach (var network in PrivateNetworks)
                {
                    options.KnownIPNetworks.Add(network);
                }
            }
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

    // Default trusted proxy networks when ForwardedHeaders:KnownProxies isn't set: loopback +
    // RFC1918/ULA, since the reverse proxy lives on the container/LAN network.
    private static readonly System.Net.IPNetwork[] PrivateNetworks =
    [
        new(IPAddress.Parse("10.0.0.0"), 8),
        new(IPAddress.Parse("172.16.0.0"), 12),
        new(IPAddress.Parse("192.168.0.0"), 16),
        new(IPAddress.Parse("127.0.0.0"), 8),
        new(IPAddress.IPv6Loopback, 128),
        new(IPAddress.Parse("fc00::"), 7),
    ];
}
