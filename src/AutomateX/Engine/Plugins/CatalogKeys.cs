namespace AutomateX.Engine.Plugins;

public static class CatalogKeys
{
    // The AutomateX release public key (minisign) that signs the first-party catalog — pinned so that
    // source verifies as Verified. If left null the first-party source is Untrusted (installs still gated by
    // AllowPluginUpload); rotate this on a key change.
    public const string? FirstParty = """
                                      untrusted comment: minisign public key 8084E408310C7A2F
                                      RWQvegwxCOSEgNCtGrGXNGe4bNyD6EtprR1YkiNBShsuFwJgnq9sT7RA
                                      """;
}
