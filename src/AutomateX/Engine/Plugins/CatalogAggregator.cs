namespace AutomateX.Engine.Plugins;

// One source's fetched state: its parsed index + the trust of its signature. Index is null when the source
// was unreachable or unparseable — those degrade silently (the rest of the catalog still lists).
public sealed record CatalogSourceResult(string SourceName, CatalogTrust Trust, CatalogIndex? Index);

// A merged, deduped catalog row for the browse UI: manifest metadata + the winning source's trust, plus
// installed/update state resolved against provenance.
public sealed record CatalogListing(
    string Id,
    string Name,
    string DisplayName,
    string? Description,
    string? Author,
    IReadOnlyList<string> Categories,
    string? Icon,
    string SourceName,
    CatalogTrust Trust,
    string LatestVersion,
    bool Installed,
    string? InstalledVersion,
    bool UpdateAvailable,
    bool AutoUpdate,
    PluginCapabilities Capabilities,
    PluginPermissions Permissions,
    IReadOnlyList<CatalogVersion> Versions);

// Merges the enabled sources into one catalog. A Blocked (tampered/wrong-key) or unreachable source
// contributes nothing. On an id collision a Verified source always wins over an Untrusted one — so an
// untrusted source can't shadow a first-party plugin by claiming a higher version — and within the same
// trust tier the highest semver wins.
public static class CatalogAggregator
{
    public static IReadOnlyList<CatalogListing> Merge(
        IEnumerable<CatalogSourceResult> sources,
        ISet<string> installedNames,
        IReadOnlyDictionary<string, string> installedVersions,
        ISet<string>? autoUpdateNames = null)
    {
        Dictionary<string, CatalogListing> byId = new(StringComparer.OrdinalIgnoreCase);

        foreach (var source in sources)
        {
            if (source.Index is null || source.Trust == CatalogTrust.Blocked)
            {
                continue;
            }

            foreach (var plugin in source.Index.Plugins)
            {
                var listing = ToListing(plugin, source, installedNames, installedVersions, autoUpdateNames);
                if (!byId.TryGetValue(listing.Id, out var existing) || Wins(listing, existing))
                {
                    byId[listing.Id] = listing;
                }
            }
        }

        return byId.Values.OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool Wins(CatalogListing candidate, CatalogListing existing)
    {
        var trust = Rank(candidate.Trust).CompareTo(Rank(existing.Trust));
        return trust != 0
            ? trust > 0
            : SemVer.Compare(candidate.LatestVersion, existing.LatestVersion) > 0;
    }

    private static int Rank(CatalogTrust trust) => trust == CatalogTrust.Verified ? 1 : 0;

    private static CatalogListing ToListing(
        CatalogPlugin plugin,
        CatalogSourceResult source,
        ISet<string> installedNames,
        IReadOnlyDictionary<string, string> installedVersions,
        ISet<string>? autoUpdateNames)
    {
        var manifest = plugin.Manifest;
        var latest = plugin.Latest.Version;
        var installed = installedNames.Contains(manifest.Name);
        installedVersions.TryGetValue(manifest.Name, out var installedVersion);
        var updateAvailable = installed
            && installedVersion is not null
            && SemVer.Compare(installedVersion, latest) < 0;

        return new CatalogListing(
            manifest.Id, manifest.Name, manifest.DisplayName, manifest.Description, manifest.Author,
            manifest.Categories, manifest.Icon, source.SourceName, source.Trust, latest,
            installed, installedVersion, updateAvailable, autoUpdateNames?.Contains(manifest.Name) ?? false,
            manifest.Capabilities, manifest.Permissions, plugin.Versions);
    }
}
