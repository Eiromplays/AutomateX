using System.Text.RegularExpressions;
using AutomateX.Modules.Triggers;
using Xunit;

namespace AutomateX.Tests;

// Rules encoded ahead of the implementation:
// - server-generated secrets: 48 lowercase hex chars (24 random bytes), unique
// - AddTo injects the secret into the trigger config, preserving existing fields
// - Validate: fixed-time match required; missing provided OR missing stored secret
//   both fail — legacy webhook triggers without a secret must be recreated.
public sealed class WebhookSecretTests
{
    [Fact]
    public void Generated_secrets_are_48_hex_and_unique()
    {
        var first = WebhookSecret.Generate();
        var second = WebhookSecret.Generate();

        Assert.Matches(new Regex("^[0-9a-f]{48}$"), first);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void AddTo_injects_secret_preserving_existing_fields()
    {
        var (configJson, secret) = WebhookSecret.AddTo("""{"note":"keep me"}""");

        Assert.Contains("keep me", configJson);
        Assert.Contains(secret, configJson);
        Assert.True(WebhookSecret.Validate(configJson, secret));
    }

    [Fact]
    public void Valid_secret_passes()
    {
        var (configJson, secret) = WebhookSecret.AddTo("{}");

        Assert.True(WebhookSecret.Validate(configJson, secret));
    }

    [Fact]
    public void Wrong_or_missing_secret_fails()
    {
        var (configJson, _) = WebhookSecret.AddTo("{}");

        Assert.False(WebhookSecret.Validate(configJson, WebhookSecret.Generate()));
        Assert.False(WebhookSecret.Validate(configJson, null));
        Assert.False(WebhookSecret.Validate(configJson, ""));
    }

    [Fact]
    public void AddTo_replaces_an_existing_secret()
    {
        var (firstConfig, firstSecret) = WebhookSecret.AddTo("{}");
        var (rotatedConfig, rotatedSecret) = WebhookSecret.AddTo(firstConfig);

        Assert.False(WebhookSecret.Validate(rotatedConfig, firstSecret));
        Assert.True(WebhookSecret.Validate(rotatedConfig, rotatedSecret));
    }

    [Fact]
    public void BuildUrl_uses_the_declared_public_base_when_configured()
    {
        var id = Guid.Parse("00000000-0000-0000-0000-000000000001");

        Assert.Equal(
            $"https://automatex.example.com/api/webhooks/{id}?secret=abc",
            WebhookSecret.BuildUrl("https://automatex.example.com/", id, "abc"));
        Assert.Equal(
            $"/api/webhooks/{id}?secret=abc",
            WebhookSecret.BuildUrl(null, id, "abc"));
    }

    [Fact]
    public void Legacy_trigger_without_stored_secret_fails()
    {
        Assert.False(WebhookSecret.Validate("{}", "anything"));
        Assert.False(WebhookSecret.Validate("not-json", "anything"));
    }
}
