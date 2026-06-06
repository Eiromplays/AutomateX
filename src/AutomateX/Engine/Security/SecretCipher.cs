using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace AutomateX.Engine.Security;

public sealed class EncryptionOptions
{
    public const string SectionName = "Encryption";

    // 32 bytes, base64. Lives in env/config only — never in the database, so a DB
    // dump alone yields ciphertext. Losing it makes stored secrets unrecoverable.
    public string? Key { get; set; }
}

public sealed class SecretCipherException(string message) : Exception(message);

// AES-256-GCM with a versioned wire format: "v1:" + base64(nonce[12] | tag[16] | ciphertext).
public sealed class SecretCipher(IOptions<EncryptionOptions> options)
{
    private const string Prefix = "v1:";
    private const string KeyGuidance = "Set Encryption:Key (env Encryption__Key) to 32 base64 bytes — generate one with: openssl rand -base64 32";

    private readonly Lazy<byte[]?> _key = new(() => ParseKey(options.Value.Key));

    public bool IsConfigured
    {
        get
        {
            try
            {
                return _key.Value is not null;
            }
            catch (SecretCipherException)
            {
                return false;
            }
        }
    }

    public string Encrypt(string plaintext)
    {
        var key = RequireKey();
        var nonce = RandomNumberGenerator.GetBytes(12);
        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var cipherBytes = new byte[plainBytes.Length];
        var tag = new byte[16];

        using var aes = new AesGcm(key, tagSizeInBytes: 16);
        aes.Encrypt(nonce, plainBytes, cipherBytes, tag);

        return Prefix + Convert.ToBase64String([.. nonce, .. tag, .. cipherBytes]);
    }

    public string Decrypt(string ciphertext)
    {
        var key = RequireKey();
        if (!ciphertext.StartsWith(Prefix, StringComparison.Ordinal))
        {
            throw new SecretCipherException("Unrecognized ciphertext format.");
        }

        byte[] data;
        try
        {
            data = Convert.FromBase64String(ciphertext[Prefix.Length..]);
        }
        catch (FormatException)
        {
            throw new SecretCipherException("Ciphertext is not valid base64.");
        }

        if (data.Length < 28)
        {
            throw new SecretCipherException("Ciphertext is too short.");
        }

        var plainBytes = new byte[data.Length - 28];
        using var aes = new AesGcm(key, tagSizeInBytes: 16);
        try
        {
            aes.Decrypt(data.AsSpan(0, 12), data.AsSpan(28), data.AsSpan(12, 16), plainBytes);
        }
        catch (CryptographicException)
        {
            throw new SecretCipherException("Decryption failed — wrong Encryption:Key or corrupted data.");
        }

        return Encoding.UTF8.GetString(plainBytes);
    }

    private byte[] RequireKey() =>
        _key.Value ?? throw new SecretCipherException($"Encryption key is not configured. {KeyGuidance}");

    private static byte[]? ParseKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(key);
        }
        catch (FormatException)
        {
            throw new SecretCipherException($"Encryption key is not valid base64. {KeyGuidance}");
        }

        return bytes.Length == 32
            ? bytes
            : throw new SecretCipherException($"Encryption key must be exactly 32 bytes. {KeyGuidance}");
    }
}
