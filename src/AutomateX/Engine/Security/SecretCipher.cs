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

    // The prior KEK, kept during rotation: set Key to the new key and PreviousKey to the old, restart,
    // then call key re-wrap to migrate every DEK to the new key — after which PreviousKey can be dropped.
    public string? PreviousKey { get; set; }
}

public sealed class SecretCipherException(string message) : Exception(message);

// AES-256-GCM with a versioned wire format: "v1:" + base64(nonce[12] | tag[16] | ciphertext).
public sealed class SecretCipher(IOptions<EncryptionOptions> options)
{
    private const string Prefix = "v1:";
    private const string KeyGuidance = "Set Encryption:Key (env Encryption__Key) to 32 base64 bytes — generate one with: openssl rand -base64 32";

    private readonly Lazy<byte[]?> _key = new(() => ParseKey(options.Value.Key));
    private readonly Lazy<byte[]?> _previousKey = new(() => ParseKey(options.Value.PreviousKey));

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

    // v1: KEK-encrypted, the original single-key format. Per-tenant (v2) lives in TenantCipher.
    public string Encrypt(string plaintext) => Prefix + Convert.ToBase64String(SealBytes(plaintext, RequireKey()));

    public string Decrypt(string ciphertext)
    {
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

        // During KEK rotation, fall back to the previous key so data wrapped under it still reads.
        try
        {
            return OpenBytes(data, RequireKey());
        }
        catch (SecretCipherException) when (_previousKey.Value is { } previous)
        {
            return OpenBytes(data, previous);
        }
    }

    // Raw AES-256-GCM, no prefix: nonce[12] | tag[16] | ciphertext. Key-agnostic so the KEK (v1) and
    // per-tenant DEK (v2) paths share one implementation.
    public static byte[] SealBytes(string plaintext, byte[] key)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var cipherBytes = new byte[plainBytes.Length];
        var tag = new byte[16];

        using var aes = new AesGcm(key, tagSizeInBytes: 16);
        aes.Encrypt(nonce, plainBytes, cipherBytes, tag);

        return [.. nonce, .. tag, .. cipherBytes];
    }

    public static string OpenBytes(ReadOnlySpan<byte> data, byte[] key)
    {
        if (data.Length < 28)
        {
            throw new SecretCipherException("Ciphertext is too short.");
        }

        var plainBytes = new byte[data.Length - 28];
        using var aes = new AesGcm(key, tagSizeInBytes: 16);
        try
        {
            aes.Decrypt(data[..12], data[28..], data.Slice(12, 16), plainBytes);
        }
        catch (CryptographicException)
        {
            throw new SecretCipherException("Decryption failed — wrong key or corrupted data.");
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
