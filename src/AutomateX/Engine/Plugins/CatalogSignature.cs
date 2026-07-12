using System.Text;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace AutomateX.Engine.Plugins;

// Trust tier of a catalog source, derived from its minisign signature against the source's pinned key.
public enum CatalogTrust
{
    Untrusted, // no pinned key, or no signature — the catalog still lists, but install needs an explicit ack
    Verified,  // the signature checks against the pinned key
    Blocked,   // a signature is present but fails the pinned key — tampering or the wrong key
}

// Verifies a minisign (Ed25519) detached signature over the catalog index. We only ever verify — the
// signing key stays with the publisher, never in the app. Ed25519 isn't in the .NET BCL, so this uses
// BouncyCastle (pure-managed, cross-platform, no native binary). Both minisign modes are supported:
// legacy "Ed" (signs the raw file) and prehashed "ED" (signs the BLAKE2b-512 of the file, the current
// minisign default). The global signature over the trusted comment is verified too, so the comment can't
// be swapped without detection.
public static class CatalogSignature
{
    public static CatalogTrust Evaluate(byte[] catalogBytes, string? signatureText, string? publicKeyText)
    {
        if (string.IsNullOrWhiteSpace(publicKeyText) || string.IsNullOrWhiteSpace(signatureText))
        {
            return CatalogTrust.Untrusted;
        }

        try
        {
            var key = ParsePublicKey(publicKeyText);
            var signature = ParseSignature(signatureText);
            return Verify(catalogBytes, signature, key) ? CatalogTrust.Verified : CatalogTrust.Blocked;
        }
        catch (FormatException)
        {
            // A key/signature is present but unparseable — refuse rather than silently downgrade to Untrusted.
            return CatalogTrust.Blocked;
        }
    }

    public static bool Verify(byte[] fileBytes, MinisignSignature signature, MinisignPublicKey key)
    {
        if (!signature.KeyId.AsSpan().SequenceEqual(key.KeyId))
        {
            return false;
        }

        var message = signature.Prehashed ? Blake2b512(fileBytes) : fileBytes;
        if (!Ed25519Verify(key.Key, message, signature.Signature))
        {
            return false;
        }

        // The global signature binds the trusted comment: Ed25519 over (signature || comment bytes).
        var comment = Encoding.UTF8.GetBytes(signature.TrustedComment);
        var global = new byte[signature.Signature.Length + comment.Length];
        signature.Signature.CopyTo(global, 0);
        comment.CopyTo(global, signature.Signature.Length);
        return Ed25519Verify(key.Key, global, signature.GlobalSignature);
    }

    public static MinisignPublicKey ParsePublicKey(string text)
    {
        var blob = DecodeKeyLine(text, 42);
        if (blob[0] != (byte)'E' || blob[1] != (byte)'d')
        {
            throw new FormatException("Unsupported minisign public key algorithm.");
        }

        return new MinisignPublicKey(blob[2..10], blob[10..42]);
    }

    public static MinisignSignature ParseSignature(string text)
    {
        var lines = text.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 4)
        {
            throw new FormatException("A minisign signature has four lines.");
        }

        var blob = FromBase64(lines[1]);
        if (blob.Length != 74)
        {
            throw new FormatException("A minisign signature blob is 74 bytes.");
        }

        var prehashed = blob[0] == (byte)'E' && blob[1] == (byte)'D';
        var legacy = blob[0] == (byte)'E' && blob[1] == (byte)'d';
        if (!prehashed && !legacy)
        {
            throw new FormatException("Unknown minisign signature algorithm.");
        }

        const string prefix = "trusted comment: ";
        if (!lines[2].StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new FormatException("A minisign signature is missing its trusted comment.");
        }

        var global = FromBase64(lines[3]);
        if (global.Length != 64)
        {
            throw new FormatException("A minisign global signature is 64 bytes.");
        }

        return new MinisignSignature(prehashed, blob[2..10], blob[10..74], lines[2][prefix.Length..], global);
    }

    // Accepts a raw base64 key or a full .pub file; returns the first non-comment line of the right length.
    private static byte[] DecodeKeyLine(string text, int expectedLength)
    {
        foreach (var line in text.ReplaceLineEndings("\n").Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0
                || trimmed.StartsWith("untrusted comment:", StringComparison.Ordinal)
                || trimmed.StartsWith("trusted comment:", StringComparison.Ordinal))
            {
                continue;
            }

            byte[] decoded;
            try
            {
                decoded = Convert.FromBase64String(trimmed);
            }
            catch (FormatException)
            {
                continue;
            }

            if (decoded.Length == expectedLength)
            {
                return decoded;
            }
        }

        throw new FormatException("No minisign key line of the expected length was found.");
    }

    private static byte[] FromBase64(string line) => Convert.FromBase64String(line.Trim());

    private static bool Ed25519Verify(byte[] publicKey, byte[] message, byte[] signature)
    {
        var verifier = new Ed25519Signer();
        verifier.Init(false, new Ed25519PublicKeyParameters(publicKey, 0));
        verifier.BlockUpdate(message, 0, message.Length);
        return verifier.VerifySignature(signature);
    }

    private static byte[] Blake2b512(byte[] data)
    {
        var digest = new Blake2bDigest(512);
        digest.BlockUpdate(data, 0, data.Length);
        var hash = new byte[64];
        digest.DoFinal(hash, 0);
        return hash;
    }
}

public sealed record MinisignPublicKey(byte[] KeyId, byte[] Key);

public sealed record MinisignSignature(
    bool Prehashed,
    byte[] KeyId,
    byte[] Signature,
    string TrustedComment,
    byte[] GlobalSignature);
