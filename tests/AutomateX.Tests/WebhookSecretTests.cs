using System.Text.RegularExpressions;
using AutomateX.Modules.Triggers;
using Xunit;

namespace AutomateX.Tests;

// Webhook auth rules:
// - server-generated secrets: 48 lowercase hex chars (24 random bytes), unique
// - AddTo injects the secret into the trigger config, preserving existing fields
// - Validate accepts an HMAC-SHA256 signature of the raw body (preferred) or the plaintext
//   secret header; both fixed-time. The secret never travels in the URL.
// - missing/legacy stored secret always fails.
public sealed class WebhookSecretTests
{
    private const string Body = """{"hello":"world"}""";

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
        Assert.True(WebhookSecret.Validate(configJson, Body, WebhookSecret.Sign(secret, Body), null));
    }

    [Fact]
    public void Sign_is_prefixed_lowercase_hmac_sha256()
    {
        Assert.Matches(new Regex("^sha256=[0-9a-f]{64}$"), WebhookSecret.Sign("secret", Body));
    }

    [Fact]
    public void Valid_signature_over_the_body_passes()
    {
        var (configJson, secret) = WebhookSecret.AddTo("{}");

        Assert.True(WebhookSecret.Validate(configJson, Body, WebhookSecret.Sign(secret, Body), null));
    }

    [Fact]
    public void Signature_over_a_different_body_or_secret_fails()
    {
        var (configJson, secret) = WebhookSecret.AddTo("{}");
        var signature = WebhookSecret.Sign(secret, Body);

        Assert.False(WebhookSecret.Validate(configJson, """{"hello":"tampered"}""", signature, null));
        Assert.False(WebhookSecret.Validate(configJson, Body, WebhookSecret.Sign(WebhookSecret.Generate(), Body), null));
    }

    [Fact]
    public void Plaintext_secret_header_is_accepted()
    {
        var (configJson, secret) = WebhookSecret.AddTo("{}");

        Assert.True(WebhookSecret.Validate(configJson, Body, null, secret));
        Assert.False(WebhookSecret.Validate(configJson, Body, null, "wrong"));
    }

    [Fact]
    public void Missing_signature_and_secret_fails()
    {
        var (configJson, _) = WebhookSecret.AddTo("{}");

        Assert.False(WebhookSecret.Validate(configJson, Body, null, null));
        Assert.False(WebhookSecret.Validate(configJson, Body, "", ""));
    }

    [Fact]
    public void AddTo_replaces_an_existing_secret()
    {
        var (firstConfig, firstSecret) = WebhookSecret.AddTo("{}");
        var (rotatedConfig, rotatedSecret) = WebhookSecret.AddTo(firstConfig);

        Assert.False(WebhookSecret.Validate(rotatedConfig, Body, WebhookSecret.Sign(firstSecret, Body), null));
        Assert.True(WebhookSecret.Validate(rotatedConfig, Body, WebhookSecret.Sign(rotatedSecret, Body), null));
    }

    [Fact]
    public void BuildUrl_has_no_secret_and_honors_the_public_base()
    {
        var id = Guid.Parse("00000000-0000-0000-0000-000000000001");

        Assert.Equal($"https://automatex.example.com/api/webhooks/{id}", WebhookSecret.BuildUrl("https://automatex.example.com/", id));
        Assert.Equal($"/api/webhooks/{id}", WebhookSecret.BuildUrl(null, id));
    }

    [Fact]
    public void Legacy_trigger_without_stored_secret_fails()
    {
        Assert.False(WebhookSecret.Validate("{}", Body, WebhookSecret.Sign("x", Body), null));
        Assert.False(WebhookSecret.Validate("not-json", Body, null, "anything"));
    }
}
