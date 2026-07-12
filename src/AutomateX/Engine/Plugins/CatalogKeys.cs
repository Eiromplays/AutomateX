namespace AutomateX.Engine.Plugins;

public static class CatalogKeys
{
    // The AutomateX release public key (minisign) that signs the first-party catalog. Null until the
    // release keypair is generated in v5.2 m7; until then the first-party source is Untrusted (installs
    // stay gated by AllowPluginUpload, as today) rather than falsely reported Verified.
    public const string? FirstParty = null;
}
