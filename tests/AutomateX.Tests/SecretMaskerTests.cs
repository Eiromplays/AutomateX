using AutomateX.Engine.Security;
using Xunit;

namespace AutomateX.Tests;

// Rules encoded ahead of the implementation:
// - every occurrence of a known secret value is replaced with ***
// - the JSON-escaped form of the secret is masked too (outputs are serialized JSON)
// - no secrets or empty/null text = unchanged; empty secret values are ignored
public sealed class SecretMaskerTests
{
    [Fact]
    public void Masks_every_exact_occurrence()
    {
        var masked = SecretMasker.MaskSecrets(
            """{"message":"Bearer s3cret","again":"s3cret"}""",
            ["s3cret"]);

        Assert.Equal("""{"message":"Bearer ***","again":"***"}""", masked);
    }

    [Fact]
    public void Masks_json_escaped_variant()
    {
        // A secret containing a quote serializes escaped — that form must be masked too.
        var secret = """pa"ss""";
        var serialized = System.Text.Json.JsonSerializer.Serialize(new { message = $"got {secret}" });

        var masked = SecretMasker.MaskSecrets(serialized, [secret]);

        Assert.DoesNotContain("pa", masked);
        Assert.Contains("***", masked);
    }

    [Fact]
    public void No_secrets_or_empty_text_is_unchanged()
    {
        Assert.Equal("text", SecretMasker.MaskSecrets("text", []));
        Assert.Null(SecretMasker.MaskSecrets(null, ["s3cret"]));
        Assert.Equal("text", SecretMasker.MaskSecrets("text", [""]));
    }
}
