using AutomateX.Modules.ApiKeys;
using AutomateX.Modules.Workspaces;
using Xunit;

namespace AutomateX.Tests;

public sealed class ApiKeyTokenTests
{
    [Fact]
    public void Minted_token_verifies_against_its_stored_hash()
    {
        var (token, prefix, secretHash) = ApiKeyToken.Mint();

        Assert.StartsWith("atx_", token);
        Assert.True(ApiKeyToken.LooksLikeToken(token));

        Assert.True(ApiKeyToken.TryParse(token, out var parsedPrefix, out var presentedHash));
        Assert.Equal(prefix, parsedPrefix);
        Assert.True(ApiKeyToken.Verify(presentedHash, secretHash));
    }

    [Fact]
    public void A_different_token_does_not_verify()
    {
        var (_, _, storedHash) = ApiKeyToken.Mint();
        var (other, _, _) = ApiKeyToken.Mint();

        Assert.True(ApiKeyToken.TryParse(other, out _, out var presentedHash));
        Assert.False(ApiKeyToken.Verify(presentedHash, storedHash));
    }

    [Fact]
    public void Neither_half_contains_the_delimiter()
    {
        var (token, prefix, _) = ApiKeyToken.Mint();
        var secret = token.Split('_')[2];

        Assert.DoesNotContain('_', prefix);
        Assert.DoesNotContain('_', secret);
        Assert.Equal(3, token.Split('_').Length);
    }

    [Theory]
    [InlineData("")]
    [InlineData("nope")]
    [InlineData("atx_only")]
    [InlineData("xxx_prefix_secret")]
    [InlineData("atx__secret")]
    [InlineData("atx_prefix_")]
    public void TryParse_rejects_malformed_tokens(string token)
    {
        Assert.False(ApiKeyToken.TryParse(token, out _, out _));
    }

    [Fact]
    public void Instance_scope_pins_role_to_owner()
    {
        var (_, prefix, hash) = ApiKeyToken.Mint();
        var key = ApiKey.Create("boot", prefix, hash, ApiKeyScope.Instance, null, null, WorkspaceRole.Viewer, "test", null);

        Assert.Equal(WorkspaceRole.Owner, key.Role);
    }

    [Fact]
    public void IsActive_reflects_expiry_and_revocation()
    {
        var now = DateTimeOffset.UtcNow;
        var (_, prefix, hash) = ApiKeyToken.Mint();

        var live = ApiKey.Create("a", prefix, hash, ApiKeyScope.Workspace, Guid.NewGuid(), null, WorkspaceRole.Editor, "t", null);
        Assert.True(live.IsActive(now));

        var expired = ApiKey.Create("b", prefix, hash, ApiKeyScope.Workspace, Guid.NewGuid(), null, WorkspaceRole.Editor, "t", now.AddMinutes(-1));
        Assert.False(expired.IsActive(now));

        live.Revoke();
        Assert.False(live.IsActive(now));
    }
}
