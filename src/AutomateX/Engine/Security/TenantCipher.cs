namespace AutomateX.Engine.Security;

// Caller-facing secret cipher. New writes are per-tenant (v2: workspace DEK); v1 (single KEK)
// ciphertext stays readable, so no forced migration. Callers pass the owning workspace.
public sealed class TenantCipher(SecretCipher cipher, DataKeyService dataKeys)
{
    private const string V2Prefix = "v2:";

    public bool IsConfigured => cipher.IsConfigured;

    public async Task<string> EncryptAsync(string plaintext, Guid workspaceId, CancellationToken cancellationToken = default)
    {
        var (version, dek) = await dataKeys.GetActiveAsync(workspaceId, cancellationToken);
        var body = SecretCipher.SealBytes(plaintext, dek);

        var framed = new byte[body.Length + 1];
        framed[0] = checked((byte)version);
        body.CopyTo(framed, 1);

        return V2Prefix + Convert.ToBase64String(framed);
    }

    public async Task<string> DecryptAsync(string ciphertext, Guid workspaceId, CancellationToken cancellationToken = default)
    {
        // v1 (KEK) or anything unrecognized: SecretCipher handles it and reports the format error.
        if (!ciphertext.StartsWith(V2Prefix, StringComparison.Ordinal))
        {
            return cipher.Decrypt(ciphertext);
        }

        byte[] framed;
        try
        {
            framed = Convert.FromBase64String(ciphertext[V2Prefix.Length..]);
        }
        catch (FormatException)
        {
            throw new SecretCipherException("Ciphertext is not valid base64.");
        }

        if (framed.Length < 1)
        {
            throw new SecretCipherException("Ciphertext is too short.");
        }

        var dek = await dataKeys.GetAsync(workspaceId, framed[0], cancellationToken);
        return SecretCipher.OpenBytes(framed.AsSpan(1), dek);
    }
}
