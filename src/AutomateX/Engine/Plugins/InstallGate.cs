namespace AutomateX.Engine.Plugins;

public enum InstallDecision
{
    Allowed,
    NeedsAcknowledgement,  // untrusted source and/or declared permissions — needs explicit consent
    RequiresInstanceAdmin, // untrusted source and the caller isn't an instance admin
    Refused,               // a signature is present but failed (tampered / wrong key)
}

// The install gate for a catalog plugin. AllowPluginUpload is still the master switch (checked at the
// endpoint); this decides the per-install trust bar on top of it. A Verified plugin with no declared
// permissions installs at the base bar; an Untrusted one needs an instance admin AND an explicit
// acknowledgement; declared permissions need an acknowledgement regardless of trust.
public static class InstallGate
{
    public static InstallDecision Evaluate(
        CatalogTrust trust, bool isInstanceAdmin, bool acknowledged, bool declaresPermissions)
    {
        if (trust == CatalogTrust.Blocked)
        {
            return InstallDecision.Refused;
        }

        if (trust == CatalogTrust.Untrusted && !isInstanceAdmin)
        {
            return InstallDecision.RequiresInstanceAdmin;
        }

        var needsAcknowledgement = trust == CatalogTrust.Untrusted || declaresPermissions;
        return needsAcknowledgement && !acknowledged
            ? InstallDecision.NeedsAcknowledgement
            : InstallDecision.Allowed;
    }
}

public static class PluginCompatibility
{
    // An unknown app version or an absent minimum means "don't block".
    public static bool IsSupported(string? appVersion, string? minAutomateXVersion) =>
        string.IsNullOrWhiteSpace(appVersion)
        || string.IsNullOrWhiteSpace(minAutomateXVersion)
        || SemVer.Compare(appVersion, minAutomateXVersion) >= 0;
}
