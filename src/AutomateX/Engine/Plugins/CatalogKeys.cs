namespace AutomateX.Engine.Plugins;

public static class CatalogKeys
{
    // The AutomateX release public key (minisign) that signs the first-party catalog. Null until the
    // release keypair is generated in v5.2 m7; until then the first-party source is Untrusted (installs
    // stay gated by AllowPluginUpload, as today) rather than falsely reported Verified.
    public const string? FirstParty = """
                                      untrusted comment: minisign public key 8084E408310C7A2F
                                      RWQvegwxCOSEgNCtGrGXNGe4bNyD6EtprR1YkiNBShsuFwJgnq9sT7RA
                                      """;
}
