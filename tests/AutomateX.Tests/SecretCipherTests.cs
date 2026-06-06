using System.Security.Cryptography;
using AutomateX.Engine.Security;
using Microsoft.Extensions.Options;
using Xunit;

namespace AutomateX.Tests;

// Rules encoded ahead of the implementation:
// - AES-256-GCM, key = 32 bytes base64 from config, ciphertext is versioned ("v1:")
// - round-trips arbitrary text; wrong key or tampered data throws SecretCipherException
// - unconfigured or malformed keys throw SecretCipherException with actionable guidance
public sealed class SecretCipherTests
{
    private static string NewKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private static SecretCipher Cipher(string? key) =>
        new(Options.Create(new EncryptionOptions { Key = key }));

    [Fact]
    public void Round_trips_plaintext()
    {
        var cipher = Cipher(NewKey());

        var encrypted = cipher.Encrypt("""{"token":"s3cret","other":"värde"}""");
        var decrypted = cipher.Decrypt(encrypted);

        Assert.StartsWith("v1:", encrypted);
        Assert.DoesNotContain("s3cret", encrypted);
        Assert.Equal("""{"token":"s3cret","other":"värde"}""", decrypted);
    }

    [Fact]
    public void Wrong_key_fails()
    {
        var encrypted = Cipher(NewKey()).Encrypt("secret");

        Assert.Throws<SecretCipherException>(() => Cipher(NewKey()).Decrypt(encrypted));
    }

    [Fact]
    public void Tampered_ciphertext_fails()
    {
        var key = NewKey();
        var encrypted = Cipher(key).Encrypt("secret");
        var tampered = encrypted[..^4] + (encrypted[^4] == 'A' ? "B" : "A") + encrypted[^3..];

        Assert.Throws<SecretCipherException>(() => Cipher(key).Decrypt(tampered));
    }

    [Fact]
    public void Unconfigured_key_throws_with_guidance()
    {
        var cipher = Cipher(key: null);

        Assert.False(cipher.IsConfigured);
        var ex = Assert.Throws<SecretCipherException>(() => cipher.Encrypt("secret"));
        Assert.Contains("openssl rand -base64 32", ex.Message);
    }

    [Fact]
    public void Invalid_key_throws()
    {
        Assert.Throws<SecretCipherException>(() => Cipher("not-base64!").Encrypt("secret"));
        Assert.Throws<SecretCipherException>(() =>
            Cipher(Convert.ToBase64String(RandomNumberGenerator.GetBytes(16))).Encrypt("secret"));
    }
}
