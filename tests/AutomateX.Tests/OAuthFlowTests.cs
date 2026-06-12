using AutomateX.Engine.Connections;
using AutomateX.Plugin.Sdk;
using Xunit;

namespace AutomateX.Tests;

public sealed class OAuthFlowTests
{
    private static OAuthConfig Config(string authorize = "https://provider.example/authorize", bool pkce = true) =>
        new(authorize, "https://provider.example/token", "client-123", "secret-xyz", ["read", "write"], pkce);

    [Fact]
    public void Pkce_challenge_matches_the_rfc7636_vector()
    {
        // RFC 7636 Appendix B.
        const string verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
        Assert.Equal("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", Pkce.Challenge(verifier));
    }

    [Fact]
    public void Pkce_verifier_is_url_safe_and_unpadded()
    {
        var verifier = Pkce.NewVerifier();
        Assert.DoesNotContain('=', verifier);
        Assert.DoesNotContain('+', verifier);
        Assert.DoesNotContain('/', verifier);
        Assert.NotEqual(Pkce.NewVerifier(), verifier); // fresh entropy each call
    }

    [Fact]
    public void Authorize_url_carries_the_standard_params()
    {
        var url = OAuthFlow.BuildAuthorizeUrl(Config(), "https://app.example/api/connections/oauth/callback", "the-state", "the-challenge");

        Assert.StartsWith("https://provider.example/authorize?", url);
        Assert.Contains("response_type=code", url);
        Assert.Contains("client_id=client-123", url);
        Assert.Contains("redirect_uri=https%3A%2F%2Fapp.example%2Fapi%2Fconnections%2Foauth%2Fcallback", url);
        Assert.Contains("scope=read%20write", url);
        Assert.Contains("state=the-state", url);
        Assert.Contains("code_challenge=the-challenge", url);
        Assert.Contains("code_challenge_method=S256", url);
    }

    [Fact]
    public void Authorize_url_omits_pkce_when_no_challenge()
    {
        var url = OAuthFlow.BuildAuthorizeUrl(Config(), "https://app.example/cb", "s", null);
        Assert.DoesNotContain("code_challenge", url);
    }

    [Fact]
    public void Authorize_url_appends_with_ampersand_when_endpoint_has_a_query()
    {
        var url = OAuthFlow.BuildAuthorizeUrl(Config("https://provider.example/authorize?tenant=acme"), "https://app/cb", "s", null);
        Assert.Contains("authorize?tenant=acme&response_type=code", url);
    }

    [Fact]
    public void Parse_token_response_reads_access_refresh_and_expiry()
    {
        var now = DateTimeOffset.Parse("2026-06-12T10:00:00Z");
        var tokens = OAuthFlow.ParseTokenResponse(
            """{"access_token":"at","refresh_token":"rt","expires_in":3600,"token_type":"bearer"}""", now);

        Assert.Equal("at", tokens.AccessToken);
        Assert.Equal("rt", tokens.RefreshToken);
        Assert.Equal(now.AddSeconds(3600), tokens.ExpiresAt);
    }

    [Fact]
    public void Parse_token_response_accepts_string_expires_in()
    {
        var now = DateTimeOffset.Parse("2026-06-12T10:00:00Z");
        var tokens = OAuthFlow.ParseTokenResponse("""{"access_token":"at","expires_in":"7200"}""", now);
        Assert.Equal(now.AddSeconds(7200), tokens.ExpiresAt);
    }

    [Fact]
    public void Parse_token_response_tolerates_missing_refresh_and_expiry()
    {
        var tokens = OAuthFlow.ParseTokenResponse("""{"access_token":"at"}""", DateTimeOffset.UtcNow);
        Assert.Equal("at", tokens.AccessToken);
        Assert.Null(tokens.RefreshToken);
        Assert.Null(tokens.ExpiresAt);
    }

    [Fact]
    public void Parse_token_response_without_access_token_throws()
    {
        Assert.Throws<InvalidOperationException>(
            () => OAuthFlow.ParseTokenResponse("""{"error":"invalid_grant"}""", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void NeedsRefresh_decision()
    {
        var now = DateTimeOffset.Parse("2026-06-12T10:00:00Z");
        var skew = TimeSpan.FromSeconds(60);

        Assert.False(OAuthFlow.NeedsRefresh(null, now, skew));                       // unknown expiry → never
        Assert.False(OAuthFlow.NeedsRefresh(now.AddMinutes(10), now, skew));         // comfortably valid
        Assert.True(OAuthFlow.NeedsRefresh(now.AddSeconds(30), now, skew));          // inside the skew window
        Assert.True(OAuthFlow.NeedsRefresh(now.AddSeconds(-5), now, skew));          // already expired
    }
}
