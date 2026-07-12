using System.Text;
using AutomateX.Engine.Plugins;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;
using Xunit;

namespace AutomateX.Tests;

// The catalog index is minisign-signed by its publisher; the operator pins the publisher's key on the
// source. Verified = signature checks; Untrusted = no key or no signature; Blocked = present but fails.
// Signatures here are produced in-test with BouncyCastle, so the suite needs no external minisign CLI.
public sealed class CatalogSignatureTests
{
    private static readonly byte[] Catalog = """{ "schemaVersion": 2, "plugins": [] }"""u8.ToArray();

    [Fact]
    public void Verified_for_a_valid_prehashed_signature()
    {
        var key = NewKey();
        var signature = Sign(key, Catalog, prehashed: true);

        Assert.Equal(CatalogTrust.Verified, CatalogSignature.Evaluate(Catalog, signature, PublicKey(key)));
    }

    [Fact]
    public void Verified_for_a_valid_legacy_signature()
    {
        var key = NewKey();
        var signature = Sign(key, Catalog, prehashed: false);

        Assert.Equal(CatalogTrust.Verified, CatalogSignature.Evaluate(Catalog, signature, PublicKey(key)));
    }

    [Fact]
    public void Untrusted_when_the_source_has_no_pinned_key()
    {
        var key = NewKey();
        var signature = Sign(key, Catalog, prehashed: true);

        Assert.Equal(CatalogTrust.Untrusted, CatalogSignature.Evaluate(Catalog, signature, publicKeyText: null));
    }

    [Fact]
    public void Untrusted_when_the_catalog_is_unsigned()
    {
        var key = NewKey();

        Assert.Equal(CatalogTrust.Untrusted, CatalogSignature.Evaluate(Catalog, signatureText: null, PublicKey(key)));
    }

    [Fact]
    public void Blocked_when_the_catalog_is_tampered()
    {
        var key = NewKey();
        var signature = Sign(key, Catalog, prehashed: true);
        var tampered = """{ "schemaVersion": 2, "plugins": [ "evil" ] }"""u8.ToArray();

        Assert.Equal(CatalogTrust.Blocked, CatalogSignature.Evaluate(tampered, signature, PublicKey(key)));
    }

    [Fact]
    public void Blocked_when_signed_by_a_different_key()
    {
        var signer = NewKey();
        var pinned = NewKey();
        var signature = Sign(signer, Catalog, prehashed: true);

        Assert.Equal(CatalogTrust.Blocked, CatalogSignature.Evaluate(Catalog, signature, PublicKey(pinned)));
    }

    [Fact]
    public void Blocked_when_the_key_is_substituted_under_the_same_key_id()
    {
        var signer = NewKey();
        var impostor = NewKey();
        var signature = Sign(signer, Catalog, prehashed: true);

        // Same key id as the signer, but a different public key — the Ed25519 check must still fail.
        var forgedPublicKey = PublicKeyText(impostor.Public, signer.KeyId);

        Assert.Equal(CatalogTrust.Blocked, CatalogSignature.Evaluate(Catalog, signature, forgedPublicKey));
    }

    [Fact]
    public void Blocked_when_the_signature_is_malformed()
    {
        var key = NewKey();

        Assert.Equal(CatalogTrust.Blocked, CatalogSignature.Evaluate(Catalog, "not-a-signature", PublicKey(key)));
    }

    [Fact]
    public void Blocked_when_the_trusted_comment_is_tampered()
    {
        var key = NewKey();
        var signature = Sign(key, Catalog, prehashed: true);
        var lines = signature.Split('\n');
        lines[2] = "trusted comment: forged";
        var tampered = string.Join('\n', lines);

        Assert.Equal(CatalogTrust.Blocked, CatalogSignature.Evaluate(Catalog, tampered, PublicKey(key)));
    }

    private sealed record Key(Ed25519PrivateKeyParameters Private, Ed25519PublicKeyParameters Public, byte[] KeyId);

    private static Key NewKey()
    {
        var priv = new Ed25519PrivateKeyParameters(new SecureRandom());
        var keyId = new byte[8];
        new SecureRandom().NextBytes(keyId);
        return new Key(priv, priv.GeneratePublicKey(), keyId);
    }

    private static string PublicKey(Key key) => PublicKeyText(key.Public, key.KeyId);

    private static string PublicKeyText(Ed25519PublicKeyParameters pub, byte[] keyId)
    {
        var blob = new byte[42];
        blob[0] = (byte)'E';
        blob[1] = (byte)'d';
        keyId.CopyTo(blob, 2);
        pub.GetEncoded().CopyTo(blob, 10);
        return $"untrusted comment: test key\n{Convert.ToBase64String(blob)}\n";
    }

    private static string Sign(Key key, byte[] file, bool prehashed, string trustedComment = "test")
    {
        var message = prehashed ? Blake2b512(file) : file;
        var signature = Ed25519(key.Private, message);

        var blob = new byte[74];
        blob[0] = (byte)'E';
        blob[1] = prehashed ? (byte)'D' : (byte)'d';
        key.KeyId.CopyTo(blob, 2);
        signature.CopyTo(blob, 10);

        var global = Ed25519(key.Private, [.. signature, .. Encoding.UTF8.GetBytes(trustedComment)]);

        return $"untrusted comment: test signature\n{Convert.ToBase64String(blob)}\n"
            + $"trusted comment: {trustedComment}\n{Convert.ToBase64String(global)}\n";
    }

    private static byte[] Ed25519(Ed25519PrivateKeyParameters priv, byte[] message)
    {
        var signer = new Ed25519Signer();
        signer.Init(true, priv);
        signer.BlockUpdate(message, 0, message.Length);
        return signer.GenerateSignature();
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
