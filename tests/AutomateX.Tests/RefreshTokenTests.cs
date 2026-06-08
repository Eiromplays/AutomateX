using System.Globalization;
using System.Security.Claims;
using AutomateX.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AutomateX.Tests;

// Refresh-token session rules, pinned before the implementation so they can't drift:
//
// Decision (ShouldRefresh):
//  - only when a refresh token exists AND the access token is at/past expiry minus leeway
//  - a missing or unparseable expires_at means "don't refresh" (no basis to act on)
//
// Token response parsing (ParseTokenResponse):
//  - a body with access_token is a success; expires_at = now + expires_in (5 min fallback)
//  - refresh_token may rotate (captured) or be omitted (null -> caller keeps the old one)
//  - an error body (no access_token) or invalid JSON yields null
//
// Apply: writes the new values onto the stored auth tokens, round-trippable expires_at.
//
// Handler (CookieTokenRefresher.ValidateAsync) glue:
//  - expired + refresh succeeds -> tokens updated, ShouldRenew = true, principal kept
//  - expired + refresh fails (null body or error body) -> RejectPrincipal + SignOut
//  - fresh access token, or no refresh token -> token endpoint is never called
public sealed class RefreshTokenTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 8, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Leeway = TimeSpan.FromMinutes(2);

    private static string ExpiresAt(TimeSpan fromNow) =>
        Now.Add(fromNow).ToString("o", CultureInfo.InvariantCulture);

    // --- ShouldRefresh -----------------------------------------------------

    [Fact]
    public void Fresh_access_token_does_not_refresh()
    {
        Assert.False(OidcTokenRefresh.ShouldRefresh("rt", ExpiresAt(TimeSpan.FromMinutes(30)), Now, Leeway));
    }

    [Fact]
    public void Within_leeway_refreshes()
    {
        // Expires in 1 minute, leeway is 2 -> inside the window.
        Assert.True(OidcTokenRefresh.ShouldRefresh("rt", ExpiresAt(TimeSpan.FromMinutes(1)), Now, Leeway));
    }

    [Fact]
    public void Already_expired_refreshes()
    {
        Assert.True(OidcTokenRefresh.ShouldRefresh("rt", ExpiresAt(TimeSpan.FromMinutes(-5)), Now, Leeway));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void No_refresh_token_never_refreshes(string? refreshToken)
    {
        Assert.False(OidcTokenRefresh.ShouldRefresh(refreshToken, ExpiresAt(TimeSpan.FromMinutes(-5)), Now, Leeway));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-date")]
    public void Missing_or_garbage_expiry_does_not_refresh(string? expiresAt)
    {
        Assert.False(OidcTokenRefresh.ShouldRefresh("rt", expiresAt, Now, Leeway));
    }

    // --- ParseTokenResponse ------------------------------------------------

    [Fact]
    public void Parses_success_with_rotation_and_expiry()
    {
        var json = """{"access_token":"new-at","refresh_token":"new-rt","id_token":"new-id","expires_in":3600}""";

        var result = OidcTokenRefresh.ParseTokenResponse(json, Now);

        Assert.NotNull(result);
        Assert.Equal("new-at", result!.AccessToken);
        Assert.Equal("new-rt", result.RefreshToken);
        Assert.Equal("new-id", result.IdToken);
        Assert.Equal(Now.AddSeconds(3600), result.ExpiresAt);
    }

    [Fact]
    public void Parses_success_without_rotation_keeps_refresh_null()
    {
        var json = """{"access_token":"new-at","expires_in":600}""";

        var result = OidcTokenRefresh.ParseTokenResponse(json, Now);

        Assert.NotNull(result);
        Assert.Null(result!.RefreshToken);
        Assert.Null(result.IdToken);
    }

    [Fact]
    public void Missing_expires_in_falls_back_to_five_minutes()
    {
        var json = """{"access_token":"new-at"}""";

        var result = OidcTokenRefresh.ParseTokenResponse(json, Now);

        Assert.NotNull(result);
        Assert.Equal(Now.AddMinutes(5), result!.ExpiresAt);
    }

    [Theory]
    [InlineData("""{"error":"invalid_grant"}""")]
    [InlineData("""{"access_token":1234}""")]
    [InlineData("not json at all")]
    [InlineData("[]")]
    public void Error_or_invalid_body_yields_null(string json)
    {
        Assert.Null(OidcTokenRefresh.ParseTokenResponse(json, Now));
    }

    // --- Apply -------------------------------------------------------------

    [Fact]
    public void Apply_updates_access_and_expiry_and_rotates_refresh()
    {
        var properties = PropertiesWith("old-at", "old-rt", ExpiresAt(TimeSpan.FromMinutes(-1)));
        var tokens = new RefreshedTokens("new-at", "new-rt", "new-id", Now.AddHours(1));

        OidcTokenRefresh.Apply(properties, tokens);

        Assert.Equal("new-at", properties.GetTokenValue("access_token"));
        Assert.Equal("new-rt", properties.GetTokenValue("refresh_token"));
        Assert.Equal("new-id", properties.GetTokenValue("id_token"));
        var roundTripped = DateTimeOffset.Parse(
            properties.GetTokenValue("expires_at")!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        Assert.Equal(Now.AddHours(1), roundTripped);
    }

    [Fact]
    public void Apply_keeps_existing_refresh_token_when_not_rotated()
    {
        var properties = PropertiesWith("old-at", "old-rt", ExpiresAt(TimeSpan.FromMinutes(-1)));
        var tokens = new RefreshedTokens("new-at", RefreshToken: null, IdToken: null, Now.AddHours(1));

        OidcTokenRefresh.Apply(properties, tokens);

        Assert.Equal("new-at", properties.GetTokenValue("access_token"));
        Assert.Equal("old-rt", properties.GetTokenValue("refresh_token"));
    }

    // --- CookieTokenRefresher (handler flow) -------------------------------

    [Fact]
    public async Task Expired_session_refreshes_and_renews()
    {
        var client = new StubTokenClient("""{"access_token":"new-at","refresh_token":"new-rt","expires_in":3600}""");
        var (context, signOut) = BuildContext("old-rt", expired: true);

        await new CookieTokenRefresher(client, NullLogger<CookieTokenRefresher>.Instance).ValidateAsync(context);

        Assert.Equal(1, client.Calls);
        Assert.Equal("old-rt", client.LastRefreshToken);
        Assert.True(context.ShouldRenew);
        Assert.NotNull(context.Principal);
        Assert.False(signOut.SignedOut);
        Assert.Equal("new-at", context.Properties.GetTokenValue("access_token"));
        Assert.Equal("new-rt", context.Properties.GetTokenValue("refresh_token"));
    }

    [Fact]
    public async Task Failed_refresh_rejects_principal_and_signs_out()
    {
        var client = new StubTokenClient(null); // token endpoint said no (revoked/expired refresh)
        var (context, signOut) = BuildContext("old-rt", expired: true);

        await new CookieTokenRefresher(client, NullLogger<CookieTokenRefresher>.Instance).ValidateAsync(context);

        Assert.Equal(1, client.Calls);
        Assert.Null(context.Principal);
        Assert.False(context.ShouldRenew);
        Assert.True(signOut.SignedOut);
    }

    [Fact]
    public async Task Error_body_also_rejects_and_signs_out()
    {
        var client = new StubTokenClient("""{"error":"invalid_grant"}""");
        var (context, signOut) = BuildContext("old-rt", expired: true);

        await new CookieTokenRefresher(client, NullLogger<CookieTokenRefresher>.Instance).ValidateAsync(context);

        Assert.Null(context.Principal);
        Assert.True(signOut.SignedOut);
    }

    [Fact]
    public async Task Fresh_session_does_not_call_token_endpoint()
    {
        var client = new StubTokenClient("""{"access_token":"new-at"}""");
        var (context, signOut) = BuildContext("old-rt", expired: false);

        await new CookieTokenRefresher(client, NullLogger<CookieTokenRefresher>.Instance).ValidateAsync(context);

        Assert.Equal(0, client.Calls);
        Assert.False(context.ShouldRenew);
        Assert.NotNull(context.Principal);
        Assert.False(signOut.SignedOut);
    }

    [Fact]
    public async Task Session_without_refresh_token_does_not_call_token_endpoint()
    {
        var client = new StubTokenClient("""{"access_token":"new-at"}""");
        var (context, _) = BuildContext(refreshToken: null, expired: true);

        await new CookieTokenRefresher(client, NullLogger<CookieTokenRefresher>.Instance).ValidateAsync(context);

        Assert.Equal(0, client.Calls);
    }

    // --- helpers -----------------------------------------------------------

    private static AuthenticationProperties PropertiesWith(string accessToken, string? refreshToken, string expiresAt)
    {
        var properties = new AuthenticationProperties();
        List<AuthenticationToken> tokens =
        [
            new() { Name = "access_token", Value = accessToken },
            new() { Name = "expires_at", Value = expiresAt },
        ];
        if (refreshToken is not null)
        {
            tokens.Add(new AuthenticationToken { Name = "refresh_token", Value = refreshToken });
        }

        properties.StoreTokens(tokens);
        return properties;
    }

    private static (CookieValidatePrincipalContext Context, CapturingAuthService SignOut) BuildContext(
        string? refreshToken, bool expired)
    {
        var signOut = new CapturingAuthService();
        var services = new ServiceCollection()
            .AddSingleton<IAuthenticationService>(signOut)
            .BuildServiceProvider();

        var httpContext = new DefaultHttpContext { RequestServices = services };

        var expiresAt = expired
            ? DateTimeOffset.UtcNow.AddMinutes(-1).ToString("o", CultureInfo.InvariantCulture)
            : DateTimeOffset.UtcNow.AddHours(1).ToString("o", CultureInfo.InvariantCulture);

        var properties = PropertiesWith("old-at", refreshToken, expiresAt);
        var principal = new ClaimsPrincipal(new ClaimsIdentity("cookie"));
        var ticket = new AuthenticationTicket(principal, properties, CookieAuthenticationDefaults.AuthenticationScheme);
        var scheme = new AuthenticationScheme(
            CookieAuthenticationDefaults.AuthenticationScheme, null, typeof(CookieAuthenticationHandler));
        var context = new CookieValidatePrincipalContext(
            httpContext, scheme, new CookieAuthenticationOptions(), ticket);

        return (context, signOut);
    }

    private sealed class StubTokenClient(string? body) : IOidcTokenClient
    {
        public int Calls { get; private set; }

        public string? LastRefreshToken { get; private set; }

        public Task<string?> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
        {
            Calls++;
            LastRefreshToken = refreshToken;
            return Task.FromResult(body);
        }
    }

    private sealed class CapturingAuthService : IAuthenticationService
    {
        public bool SignedOut { get; private set; }

        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
        {
            SignedOut = true;
            return Task.CompletedTask;
        }

        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme) =>
            throw new NotSupportedException();

        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) =>
            throw new NotSupportedException();

        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) =>
            throw new NotSupportedException();

        public Task SignInAsync(
            HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? properties) =>
            throw new NotSupportedException();
    }
}
