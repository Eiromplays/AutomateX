namespace AutomateX.Engine.Plugins;

// Chooses the version an auto-update should move to: the highest available version that is newer than the
// installed one, shares its major (no automatic major bumps), and is compatible with the running app.
// Returns null when the plugin is already current or nothing qualifies. Trust is the caller's gate — the
// auto-updater only ever considers a Verified source.
public static class AutoUpdate
{
    public static CatalogVersion? Choose(
        string installedVersion, IReadOnlyList<CatalogVersion> available, string? appVersion)
    {
        var installedMajor = Major(installedVersion);
        CatalogVersion? best = null;

        foreach (var candidate in available)
        {
            if (Major(candidate.Version) != installedMajor
                || SemVer.Compare(candidate.Version, installedVersion) <= 0
                || !PluginCompatibility.IsSupported(appVersion, candidate.MinAutomateXVersion))
            {
                continue;
            }

            if (best is null || SemVer.Compare(candidate.Version, best.Version) > 0)
            {
                best = candidate;
            }
        }

        return best;
    }

    private static int Major(string version) =>
        int.Parse(version.Split('+')[0].Split('-')[0].Split('.')[0]);
}
