using System.Security.Cryptography;
using System.Text;

namespace AutomateX.Modules.ApiKeys;

// Token shape: atx_<prefix>_<secret>. Prefix is a non-secret, indexed lookup id; only SHA-256(secret)
// is persisted. Hex-encoded so neither half can contain the '_' delimiter. The full token is shown once
// at creation and is never retrievable — auth re-derives the hash and fixed-time compares it.
public static class ApiKeyToken
{
    public const string Marker = "atx";
    private const int PrefixBytes = 6;   // 12 hex chars — ample for a unique lookup id
    private const int SecretBytes = 32;  // 256-bit secret

    public static bool LooksLikeToken(string value) =>
        value.StartsWith(Marker + "_", StringComparison.Ordinal);

    // The full one-time token plus the (prefix, secretHash) to persist.
    public static (string Token, string Prefix, byte[] SecretHash) Mint()
    {
        var prefix = Hex(RandomNumberGenerator.GetBytes(PrefixBytes));
        var secret = Hex(RandomNumberGenerator.GetBytes(SecretBytes));
        return ($"{Marker}_{prefix}_{secret}", prefix, Hash(secret));
    }

    // Splits a presented token into its lookup prefix and the secret's hash (to compare with storage).
    public static bool TryParse(string token, out string prefix, out byte[] secretHash)
    {
        prefix = "";
        secretHash = [];

        var parts = token.Split('_');
        if (parts.Length != 3 || parts[0] != Marker || parts[1].Length == 0 || parts[2].Length == 0)
        {
            return false;
        }

        prefix = parts[1];
        secretHash = Hash(parts[2]);
        return true;
    }

    public static bool Verify(byte[] presentedSecretHash, byte[] storedHash) =>
        CryptographicOperations.FixedTimeEquals(presentedSecretHash, storedHash);

    private static byte[] Hash(string secret) => SHA256.HashData(Encoding.UTF8.GetBytes(secret));

    private static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();
}
