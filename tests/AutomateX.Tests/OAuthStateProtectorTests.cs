using System.Security.Cryptography;
using AutomateX.Engine.Connections;
using AutomateX.Engine.Security;
using AutomateX.Modules.Connections;
using Microsoft.Extensions.Options;
using Xunit;

namespace AutomateX.Tests;

public sealed class OAuthStateProtectorTests
{
    private static OAuthStateProtector Protector(out SecretCipher cipher)
    {
        cipher = new SecretCipher(Options.Create(new EncryptionOptions
        {
            Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
        }));
        return new OAuthStateProtector(cipher);
    }

    private static OAuthStateData Sample(long? issuedAt = null) =>
        new(Guid.CreateVersion7(), Guid.CreateVersion7(), "verifier-xyz", issuedAt ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds());

    [Fact]
    public void Round_trip_recovers_the_state()
    {
        var protector = Protector(out _);
        var data = Sample();

        var recovered = protector.Unprotect(protector.Protect(data));

        Assert.Equal(data, recovered);
    }

    [Fact]
    public void Tampered_state_is_rejected()
    {
        var protector = Protector(out _);
        var token = protector.Protect(Sample());

        Assert.Null(protector.Unprotect(token + "x"));
        Assert.Null(protector.Unprotect("not-even-ciphertext"));
    }

    [Fact]
    public void State_from_a_different_key_is_rejected()
    {
        var a = Protector(out _);
        var b = Protector(out _);

        Assert.Null(b.Unprotect(a.Protect(Sample())));
    }

    [Fact]
    public void Expired_or_future_dated_state_is_rejected()
    {
        var protector = Protector(out _);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        Assert.Null(protector.Unprotect(protector.Protect(Sample(now - 3600)))); // 1h old
        Assert.Null(protector.Unprotect(protector.Protect(Sample(now + 600))));  // 10m in the future
    }
}

public sealed class OAuth2ConnectionTypeTests
{
    [Fact]
    public void BuildOAuthConfig_maps_values_and_splits_scopes()
    {
        var config = new OAuth2ConnectionType().BuildOAuthConfig(new Dictionary<string, string>
        {
            ["clientId"] = "cid",
            ["clientSecret"] = "secret",
            ["authorizeUrl"] = "https://p.example/authorize",
            ["tokenUrl"] = "https://p.example/token",
            ["scopes"] = "repo  read:user",
        });

        Assert.Equal("https://p.example/authorize", config.AuthorizationEndpoint);
        Assert.Equal("https://p.example/token", config.TokenEndpoint);
        Assert.Equal("cid", config.ClientId);
        Assert.Equal("secret", config.ClientSecret);
        Assert.Equal(["repo", "read:user"], config.Scopes);
        Assert.True(config.UsePkce);
    }

    [Fact]
    public void BuildOAuthConfig_tolerates_missing_values()
    {
        var config = new OAuth2ConnectionType().BuildOAuthConfig(new Dictionary<string, string>());

        Assert.Equal("", config.ClientId);
        Assert.Empty(config.Scopes);
    }
}
